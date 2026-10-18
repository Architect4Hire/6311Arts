import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, timeout } from 'rxjs';
import { MOCK_CLASSES } from '../data/mock-classes';
import { MOCK_EXHIBITIONS } from '../data/mock-exhibitions';
import { MOCK_ARTISTS, MOCK_PRODUCTS, slugify } from '../data/mock-shop';
import { ArtClass, Artist, CmsPage, Exhibition, Product } from './models';
import { API_CONFIG } from './site';

/** Categories in WooCommerce that describe what a product is rather than who made it. */
const TYPE_CATEGORIES = new Set([
  'artists', 'ceramics', 'jewelry', 'prints', 'painting', 'sculpture', 'books-magazines', 'branded-merchandise',
  'classes', 'classes-workshops-events', 'events', 'memberships', 'uncategorized',
]);

export interface ProductQuery {
  category?: string;
  artist?: string;
  search?: string;
  page?: number;
  perPage?: number;
  orderby?: 'date' | 'price' | 'title' | 'popularity';
  order?: 'asc' | 'desc';
}

export interface ProductPage {
  items: Product[];
  total: number;
  totalPages: number;
}

/**
 * Reads 6311arts content from WordPress. The shop comes from the public WooCommerce Store API, classes, exhibitions
 * and artists from the small `6311/v1` endpoints in wordpress/6311-headless.php. Every call falls back to bundled
 * sample data so a slow or unreachable WordPress never takes the marketing site down.
 *
 * GET responses made during server rendering are handed to the browser by Angular's HTTP transfer cache, so pages
 * hydrate without fetching twice.
 */
@Injectable({ providedIn: 'root' })
export class WpApi {
  private http = inject(HttpClient);
  private cfg = inject(API_CONFIG);

  /** Where checkout, cart and account live. Staff keep using WooCommerce for orders and tickets. */
  readonly origin = this.cfg.wpOrigin;

  cartUrl(): string {
    return `${this.origin}/cart/`;
  }

  /** Adds an item to the WooCommerce cart and lands on checkout. Falls back to the product page when the id is unknown. */
  buyUrl(item: { id?: number; slug: string }): string {
    return item.id ? `${this.origin}/checkout/?add-to-cart=${item.id}` : `${this.origin}/product/${item.slug}/`;
  }

  products(q: ProductQuery = {}): Observable<ProductPage> {
    const perPage = q.perPage ?? 12;
    const page = q.page ?? 1;
    let params = new HttpParams().set('per_page', perPage).set('page', page);
    if (q.category) params = params.set('category', q.category);
    if (q.artist) params = params.set('category', q.artist);
    if (q.search) params = params.set('search', q.search);
    if (q.orderby) params = params.set('orderby', q.orderby).set('order', q.order ?? 'desc');

    const fallback = () => paginate(filterMock(q), page, perPage);
    return this.get<StoreProduct[]>('/wp-json/wc/store/v1/products', params, true).pipe(
      map((res) => {
        if (!res) return fallback();
        const r = res as HttpResponse<StoreProduct[]>;
        const items = (r.body ?? []).map(toProduct);
        const total = Number(r.headers.get('X-WP-Total') ?? items.length);
        return { items, total, totalPages: Number(r.headers.get('X-WP-TotalPages') ?? 1) };
      }),
    );
  }

  product(slug: string): Observable<Product | undefined> {
    const params = new HttpParams().set('slug', slug);
    return this.get<StoreProduct[]>('/wp-json/wc/store/v1/products', params).pipe(
      map((list) => (list ? (list as StoreProduct[]).map(toProduct)[0] : MOCK_PRODUCTS.find((p) => p.slug === slug))),
    );
  }

  /** Upcoming classes, workshops and events, soonest first. */
  classes(): Observable<ArtClass[]> {
    return this.get<ArtClass[]>('/wp-json/6311/v1/classes').pipe(
      map((list) => ((list as ArtClass[] | null) ?? MOCK_CLASSES).slice().sort((a, b) => a.start.localeCompare(b.start))),
    );
  }

  classBySlug(slug: string): Observable<ArtClass | undefined> {
    return this.classes().pipe(map((all) => all.find((c) => c.slug === slug)));
  }

  exhibitions(): Observable<Exhibition[]> {
    return this.get<Exhibition[]>('/wp-json/6311/v1/exhibitions').pipe(
      map((list) => ((list as Exhibition[] | null) ?? MOCK_EXHIBITIONS).slice().sort((a, b) => b.start.localeCompare(a.start))),
    );
  }

  artists(): Observable<Artist[]> {
    return this.get<Artist[]>('/wp-json/6311/v1/artists').pipe(map((list) => (list as Artist[] | null) ?? MOCK_ARTISTS));
  }

  /** Plain WordPress pages (privacy policy, terms) so staff edit legal copy in wp-admin. */
  page(slug: string): Observable<CmsPage | undefined> {
    const params = new HttpParams().set('slug', slug).set('_fields', 'slug,title,content');
    return this.get<WpPage[]>('/wp-json/wp/v2/pages', params).pipe(
      map((list) => {
        const p = (list as WpPage[] | null)?.[0];
        return p ? { slug: p.slug, title: decode(p.title.rendered), html: p.content.rendered } : undefined;
      }),
    );
  }

  /** GET with a timeout. Resolves to null on any failure so callers can swap in sample data. */
  private get<T>(path: string, params?: HttpParams, withHeaders = false): Observable<T | HttpResponse<T> | null> {
    if (this.cfg.mockOnly) return of(null);
    const url = `${this.origin}${path}`;
    const req: Observable<T | HttpResponse<T>> = withHeaders
      ? this.http.get<T>(url, { params, observe: 'response' })
      : this.http.get<T>(url, { params });
    return req.pipe(
      timeout(this.cfg.timeoutMs),
      catchError(() => of(null)),
    );
  }
}

// ---- WooCommerce Store API mapping -------------------------------------------------------------

interface StoreProduct {
  id: number;
  slug: string;
  name: string;
  short_description: string;
  description: string;
  prices: { price: string; currency_minor_unit: number };
  images: { src: string; thumbnail: string; alt: string }[];
  categories: { id: number; name: string; slug: string }[];
  is_in_stock: boolean;
}

interface WpPage {
  slug: string;
  title: { rendered: string };
  content: { rendered: string };
}

function toProduct(p: StoreProduct): Product {
  const artistCat = p.categories.find((c) => !TYPE_CATEGORIES.has(c.slug));
  const minor = p.prices.currency_minor_unit ?? 2;
  return {
    id: p.id,
    slug: p.slug,
    name: decode(p.name),
    artist: artistCat ? { name: decode(artistCat.name), slug: artistCat.slug } : undefined,
    price: Math.round(Number(p.prices.price) * Math.pow(10, 2 - minor)),
    image: p.images[0]?.src,
    categories: p.categories.filter((c) => TYPE_CATEGORIES.has(c.slug)).map((c) => c.slug),
    inStock: p.is_in_stock,
    description: stripTags(p.short_description || p.description),
  };
}

function filterMock(q: ProductQuery): Product[] {
  let list = MOCK_PRODUCTS.slice();
  if (q.category) list = list.filter((p) => p.categories.includes(q.category!));
  if (q.artist) list = list.filter((p) => p.artist?.slug === q.artist);
  if (q.search) {
    const s = q.search.toLowerCase();
    list = list.filter((p) => `${p.name} ${p.artist?.name}`.toLowerCase().includes(s));
  }
  if (q.orderby === 'price') list.sort((a, b) => (q.order === 'asc' ? a.price - b.price : b.price - a.price));
  if (q.orderby === 'title') list.sort((a, b) => a.name.localeCompare(b.name));
  return list;
}

function paginate(list: Product[], page: number, perPage: number): ProductPage {
  return {
    items: list.slice((page - 1) * perPage, page * perPage),
    total: list.length,
    totalPages: Math.max(1, Math.ceil(list.length / perPage)),
  };
}

const ENTITIES: Record<string, string> = { '&amp;': '&', '&#8211;': '–', '&#8217;': '’', '&#8220;': '“', '&#8221;': '”', '&#038;': '&', '&quot;': '"' };

export function decode(s: string): string {
  return s.replace(/&[#a-z0-9]+;/gi, (m) => ENTITIES[m] ?? m);
}

function stripTags(html: string): string {
  return decode(html.replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim());
}

export { slugify };
