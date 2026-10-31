import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, RESPONSE_INIT, inject } from '@angular/core';
import { Observable, catchError, map, of, timeout } from 'rxjs';
import {
  ArtClass,
  Artist,
  ArtistPage,
  ClassPage,
  CmsPage,
  Exhibition,
  Instructor,
  MembershipTier,
  Product,
  ProductPage,
} from '../core/models';
import { MOCK_CLASSES } from '../data/mock-classes';
import { MOCK_EXHIBITIONS } from '../data/mock-exhibitions';
import { MOCK_INSTRUCTORS } from '../data/instructors';
import { MEMBERSHIPS, MOCK_ARTISTS, MOCK_PRODUCTS } from '../data/mock-shop';
import { decodeArray } from '../models/decode';
import {
  SitePreview,
  decodeSiteArtistPage,
  decodeSiteArtistSummary,
  decodeSiteClass,
  decodeSiteClassSummary,
  decodeSiteExhibition,
  decodeSiteExhibitionSummary,
  decodeSiteInstructor,
  decodeSiteMembershipPlan,
  decodeSitePage,
  decodeSitePreview,
  decodeSiteShopItem,
  decodeSiteShopPage,
} from '../models/site';
import { SITE_API_CONFIG } from './site-api.config';
import {
  toArtClasses,
  toArtist,
  toArtistPage,
  toClassPage,
  toCmsPage,
  toExhibition,
  toExhibitions,
  toInstructor,
  toMembershipTier,
  toProduct,
  toProductPage,
} from './site-mappers';

/** The site read API, always addressed relatively: the browser through the gateway, the server through `GatewayBackend`. */
export const SITE_API_BASE = '/api/v1/site';

/** The orders `/site/shop` accepts (`ShopPolicy.SiteSorts`); leaving it out means `newest`. */
export type ShopSort = 'newest' | 'price-asc' | 'price-desc' | 'title';

export interface ProductQuery {
  category?: string;
  artist?: string;
  /** Newest first when absent. */
  sort?: ShopSort;
  /** Where to resume, from the previous page's `nextCursor`. */
  cursor?: string;
  limit?: number;
}

/** What one read came to: the decoded body, a real 404, or anything else that went wrong. */
type Read<T> = { kind: 'ok'; value: T } | { kind: 'not-found' } | { kind: 'failed' };

/**
 * Reads the site's content from `/api/v1/site/*` (ADR 0042) and hands the pages the shapes they render. Replaces the
 * spike's WordPress service; the method names are the ones the pages already called.
 *
 * - **Relative URLs only** (frontend.md). The HTTP transfer cache keys a request by its URL, so the server's render
 *   and the browser's hydration ask for the same `/api/v1/site/...` and the browser does not fetch twice; the server
 *   alone sends it to the gateway's origin (`GatewayBackend`).
 * - **A failed read** is an empty list or a missing record, and on the server the response becomes a `503`
 *   `no-store`, so a blip is neither cached as the page nor indexed as a 404. With the mock flag on it is the bundled
 *   sample data instead. A real 404 is just a missing record: the page shows its own not-found.
 */
@Injectable({ providedIn: 'root' })
export class SiteApi {
  private readonly http = inject(HttpClient);
  private readonly cfg = inject(SITE_API_CONFIG);
  private readonly response = inject(RESPONSE_INIT, { optional: true });

  /** Every upcoming run of every class, soonest first. */
  classes(): Observable<ArtClass[]> {
    return this.read('/classes', (body) => decodeArray(body, decodeSiteClassSummary)).pipe(
      map((r) =>
        r.kind === 'ok'
          ? toArtClasses(r.value)
          : this.fallback(() => sortByStart(MOCK_CLASSES), []),
      ),
    );
  }

  /**
   * One class and its dates (ADR 0065): `undefined` when there is no such published class, `null` when the read
   * failed. A class with no run still to come is a page all the same — the copy is what it is for.
   */
  classPage(slug: string): Observable<ClassPage | undefined | null> {
    return this.read(`/classes/${encodeURIComponent(slug)}`, decodeSiteClass).pipe(
      map((r) =>
        r.kind === 'ok'
          ? toClassPage(r.value)
          : r.kind === 'not-found'
            ? undefined
            : this.fallback(() => mockClassPage(slug), null),
      ),
    );
  }

  instructors(): Observable<Instructor[]> {
    return this.read('/instructors', (body) => decodeArray(body, decodeSiteInstructor)).pipe(
      map((r) =>
        r.kind === 'ok' ? r.value.map(toInstructor) : this.fallback(() => MOCK_INSTRUCTORS, []),
      ),
    );
  }

  /** Newest opening first. */
  exhibitions(): Observable<Exhibition[]> {
    return this.read('/exhibitions', (body) => decodeArray(body, decodeSiteExhibitionSummary)).pipe(
      map((r) =>
        r.kind === 'ok'
          ? toExhibitions(r.value)
          : this.fallback(
              () => MOCK_EXHIBITIONS.slice().sort((a, b) => b.start.localeCompare(a.start)),
              [],
            ),
      ),
    );
  }

  /**
   * One show and its write-up (ADR 0064): `undefined` when there is no such published exhibition, and `null` when
   * the read failed.
   *
   * The two are different answers and the page gives different pages for them. A missing show is the site's 404;
   * a failed read must not be, or a blip would tell a crawler that a published show is gone — the render is a 503
   * instead (frontend.md).
   */
  exhibition(slug: string): Observable<Exhibition | undefined | null> {
    return this.read(`/exhibitions/${encodeURIComponent(slug)}`, decodeSiteExhibition).pipe(
      map((r) =>
        r.kind === 'ok'
          ? toExhibition(r.value)
          : r.kind === 'not-found'
            ? undefined
            : this.fallback(() => MOCK_EXHIBITIONS.find((e) => e.slug === slug) ?? null, null),
      ),
    );
  }

  artists(): Observable<Artist[]> {
    return this.read('/artists', (body) => decodeArray(body, decodeSiteArtistSummary)).pipe(
      map((r) => (r.kind === 'ok' ? r.value.map(toArtist) : this.fallback(() => MOCK_ARTISTS, []))),
    );
  }

  /**
   * One artist and the first page of their work, or `undefined` when there is no such published artist. A failed read
   * is a page built from the slug alone, with no work, so the page renders while the server answers 503.
   */
  artist(slug: string): Observable<ArtistPage | undefined> {
    return this.read(`/artists/${encodeURIComponent(slug)}`, decodeSiteArtistPage).pipe(
      map((r) =>
        r.kind === 'ok'
          ? toArtistPage(r.value)
          : r.kind === 'not-found'
            ? undefined
            : this.fallback(() => mockArtistPage(slug), {
                artist: { slug, name: nameFromSlug(slug) },
                works: [],
                nextCursor: null,
              }),
      ),
    );
  }

  /** One page of the shop, with how many works the filters match in all. */
  products(q: ProductQuery = {}): Observable<ProductPage> {
    let params = new HttpParams();
    if (q.category) params = params.set('category', q.category);
    if (q.artist) params = params.set('artist', q.artist);
    if (q.sort) params = params.set('sort', q.sort);
    if (q.cursor) params = params.set('cursor', q.cursor);
    if (q.limit) params = params.set('limit', q.limit);
    return this.read('/shop', decodeSiteShopPage, params).pipe(
      map((r) =>
        r.kind === 'ok'
          ? toProductPage(r.value)
          : this.fallback(() => mockProducts(q), { items: [], total: 0, nextCursor: null }),
      ),
    );
  }

  product(slug: string): Observable<Product | undefined> {
    return this.read(`/shop/${encodeURIComponent(slug)}`, decodeSiteShopItem).pipe(
      map((r) =>
        r.kind === 'ok'
          ? toProduct(r.value)
          : r.kind === 'not-found'
            ? undefined
            : this.fallback(() => MOCK_PRODUCTS.find((p) => p.slug === slug), undefined),
      ),
    );
  }

  /** Every published tier, in the order staff set. */
  memberships(): Observable<MembershipTier[]> {
    return this.read('/memberships', (body) => decodeArray(body, decodeSiteMembershipPlan)).pipe(
      map((r) =>
        r.kind === 'ok' ? r.value.map(toMembershipTier) : this.fallback(() => MEMBERSHIPS, []),
      ),
    );
  }

  /** A CMS page (privacy, terms), or `undefined` when there is none: the page shows its holding copy. */
  page(slug: string): Observable<CmsPage | undefined> {
    return this.read(`/pages/${encodeURIComponent(slug)}`, decodeSitePage).pipe(
      map((r) =>
        r.kind === 'ok'
          ? toCmsPage(r.value)
          : r.kind === 'not-found'
            ? undefined
            : this.fallback(() => undefined, undefined),
      ),
    );
  }

  /**
   * The draft a preview link names (ADR 0036), or `undefined` when the token is no longer usable.
   *
   * Everything unusable is one answer, as the API's is: expired, tampered with, or a record since deleted all come
   * back `undefined`, and the preview page says the link no longer works. There is no sample-data fallback and no
   * 503 — a preview is a staff tool, and a draft is never cached or indexed either way.
   */
  preview(token: string): Observable<SitePreview | undefined> {
    return this.read(`/preview/${encodeURIComponent(token)}`, decodeSitePreview).pipe(
      map((r) => (r.kind === 'ok' ? r.value : undefined)),
    );
  }

  /** GET with a timeout, decoded at the boundary. Never errors: the outcome says what happened. */
  private read<T>(
    path: string,
    decode: (body: unknown) => T | null,
    params?: HttpParams,
  ): Observable<Read<T>> {
    return this.http.get<unknown>(SITE_API_BASE + path, { params }).pipe(
      timeout(this.cfg.timeoutMs),
      map((body): Read<T> => {
        const value = decode(body);
        return value === null ? { kind: 'failed' } : { kind: 'ok', value };
      }),
      catchError((error: unknown) =>
        of<Read<T>>(
          error instanceof HttpErrorResponse && error.status === 404
            ? { kind: 'not-found' }
            : { kind: 'failed' },
        ),
      ),
    );
  }

  /** What a failed read shows: the sample data with the flag on; otherwise nothing, and the render is a 503. */
  private fallback<T>(mock: () => T, empty: T): T {
    if (this.cfg.mockFallback) return mock();
    this.markUnavailable();
    return empty;
  }

  private markUnavailable(): void {
    if (!this.response) return;
    this.response.status = 503;
    const headers = new Headers(this.response.headers);
    headers.set('Cache-Control', 'no-store');
    this.response.headers = headers;
  }
}

function sortByStart(list: ArtClass[]): ArtClass[] {
  return list.slice().sort((a, b) => a.start.localeCompare(b.start));
}

/** The sample class behind one slug, for the mock fallback: its runs are the sample calendar's. */
function mockClassPage(slug: string): ClassPage | null {
  const runs = MOCK_CLASSES.filter((c) => c.slug === slug);
  const first = runs[0];
  return first
    ? {
        slug,
        title: first.title,
        summary: first.summary,
        type: first.type,
        level: first.level,
        image: first.image,
        runs: sortByStart(runs),
      }
    : null;
}

/** "jane-doe" as "Jane Doe": the best name a page has for an artist it could not read. */
function nameFromSlug(slug: string): string {
  return slug.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

/** A sample artist's page: their roster entry, else one named from the slug, with their sample work. */
function mockArtistPage(slug: string): ArtistPage {
  const works = mockProducts({ artist: slug });
  return {
    artist: MOCK_ARTISTS.find((a) => a.slug === slug) ?? { slug, name: nameFromSlug(slug) },
    works: works.items,
    nextCursor: works.nextCursor,
  };
}

/** The sample shop, filtered and paged the way the API pages it; the cursor is just an offset here. */
function mockProducts(q: ProductQuery): ProductPage {
  let list = MOCK_PRODUCTS.slice();
  if (q.category) list = list.filter((p) => p.categories.includes(q.category!));
  if (q.artist) list = list.filter((p) => p.artist?.slug === q.artist);
  const price = (p: Product) => p.price ?? Number.POSITIVE_INFINITY;
  if (q.sort === 'price-asc') list.sort((a, b) => price(a) - price(b));
  if (q.sort === 'price-desc') list.sort((a, b) => price(b) - price(a));
  if (q.sort === 'title') list.sort((a, b) => a.name.localeCompare(b.name));
  const limit = q.limit ?? 20;
  const offset = Number(q.cursor ?? 0) || 0;
  const items = list.slice(offset, offset + limit);
  return {
    items,
    total: list.length,
    nextCursor: offset + limit < list.length ? String(offset + limit) : null,
  };
}
