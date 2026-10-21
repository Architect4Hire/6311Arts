import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map, switchMap, tap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { LegacyStore } from '../core/legacy-store';
import { IN_PAGE } from '../core/navigation';
import { Seo, productLd } from '../core/seo.service';
import { SITE } from '../core/site';
import { ShopSort, SiteApi } from '../services/site-api.service';
import { SHOP_PHOTOS } from '../data/venue-photos';
import { ArtImage } from '../shared/art-image';
import { ProductCard } from '../shared/cards';
import { MoneyPipe } from '../shared/format';
import { Hero } from '../shared/hero';
import { NotFound } from './not-found';

const CATEGORIES = [
  { slug: '', label: 'Everything' },
  { slug: 'jewelry', label: 'Jewelry' },
  { slug: 'prints', label: 'Prints' },
  { slug: 'ceramics', label: 'Ceramics' },
  { slug: 'painting', label: 'Painting' },
  { slug: 'sculpture', label: 'Sculpture' },
  { slug: 'books-magazines', label: 'Books' },
];

const PAGE_SIZE = 12;
const SORTS: readonly string[] = ['price-asc', 'price-desc', 'title'] satisfies ShopSort[];

/**
 * Shop grid. Category, sort and page live in the query string so every view is a crawlable, shareable URL. The API
 * pages by cursor (ADR 0042), so a later page's address carries its cursor beside the page number it shows; the
 * count and "Page N of M" come from the list's total.
 */
@Component({
  selector: 'app-shop',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ProductCard, UiIcon],
  template: `
    <app-hero
      title="Shop local art"
      eyebrow="780+ original works"
      lead="Every piece is made by an artist who lives within six miles of the gallery. Buy online or see it in person on Clark Street."
      [images]="shopPhotos"
    />
    <section class="band band--white" id="content">
      <div class="container">
        <div class="filters">
          @for (c of cats; track c.slug) {
            <a
              class="chip"
              [class.is-on]="cat() === c.slug"
              [href]="href({ category: c.slug || null, page: null, cursor: null })"
              (click)="stay($event, { category: c.slug || null, page: null, cursor: null })"
              >{{ c.label }}</a
            >
          }
          <span class="filters__sep"></span>
          <label class="select">
            <span class="sr-only">Sort by</span>
            <select [value]="sort()" (change)="setSort($any($event.target).value)">
              <option value="">Newest</option>
              <option value="price-asc">Price, low to high</option>
              <option value="price-desc">Price, high to low</option>
              <option value="title">A to Z</option>
            </select>
          </label>
        </div>
        <!-- Paging lands here, not on the hero: focused (for a keyboard or screen reader) and scrolled to. -->
        <p class="filters__count" id="works" tabindex="-1" #count>{{ result()?.total ?? 0 }} works</p>
        <div class="grid-products">
          @for (p of result()?.items ?? []; track p.id) {
            <app-product-card [item]="p" />
          }
        </div>
        @if (totalPages() > 1) {
          <nav class="pager" aria-label="Shop pages">
            @if (previous(); as prev) {
              <a
                class="btn btn-border-dark pager__prev"
                [href]="href(prev.query)"
                [attr.rel]="prev.first ? null : 'prev'"
                (click)="turn($event, prev.query)"
                >{{ prev.first ? 'First page' : 'Previous' }}</a
              >
            }
            <span class="pager__status">Page {{ page() }} of {{ totalPages() }}</span>
            @if (result()?.nextCursor; as next) {
              <a
                class="btn btn-dark pager__next"
                [href]="href({ page: page() + 1, cursor: next })"
                rel="next"
                (click)="turn($event, { page: page() + 1, cursor: next })"
                >Next</a
              >
            }
          </nav>
        }
      </div>
    </section>
    <section class="band band--light">
      <div class="container cols-3 text-center">
        <div class="feature feature--dark">
          <ui-icon name="heart" [size]="56" [stroke]="1" />
          <h3 class="feature__title">Support artists</h3>
          <p>Every sale supports the local artist who made the work.</p>
        </div>
        <div class="feature feature--dark">
          <ui-icon name="pin" [size]="56" [stroke]="1" />
          <h3 class="feature__title">See it in person</h3>
          <p>Most pieces are on display in the gallery, Wednesday to Sunday.</p>
        </div>
        <div class="feature feature--dark">
          <ui-icon name="gift" [size]="56" [stroke]="1" />
          <h3 class="feature__title">14-day returns</h3>
          <p>Shop items come back undamaged with a receipt for a full refund.</p>
        </div>
      </div>
    </section>
  `,
})
export class Shop {
  private api = inject(SiteApi);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private qp = toSignal(this.route.queryParamMap, { requireSync: true });
  protected cats = CATEGORIES;
  protected cat = computed(() => this.qp().get('category') ?? '');
  protected sort = computed(() => this.qp().get('sort') ?? '');
  private cursor = computed(() => this.qp().get('cursor'));
  /** Only a cursor reaches past the first page; a bare `?page=3` (an old link) is the first page. */
  protected page = computed(() =>
    this.cursor() ? Math.max(2, Number(this.qp().get('page')) || 2) : 1,
  );

  /**
   * The cursor each page was reached with, for a Previous link. Memory only, and per filter and order: someone who
   * lands on page 5 from a shared link has no way back but to the first page, so that is what the link offers them.
   */
  private visited = new Map<string, string>();
  private visitKey(page: number): string {
    return `${this.cat()}|${this.sort()}|${page}`;
  }

  protected result = toSignal(
    inject(ActivatedRoute).queryParamMap.pipe(
      switchMap((q) => {
        const sort = q.get('sort') ?? '';
        const cursor = q.get('cursor') ?? undefined;
        return this.api
          .products({
            category: q.get('category') ?? undefined,
            sort: SORTS.includes(sort) ? (sort as ShopSort) : undefined,
            cursor,
            limit: PAGE_SIZE,
          })
          .pipe(
            tap(() => {
              if (cursor) this.visited.set(this.visitKey(this.page()), cursor);
              if (this.turning) this.landOnResults();
            }),
          );
      }),
    ),
  );
  protected totalPages = computed(() =>
    Math.max(1, Math.ceil((this.result()?.total ?? 0) / PAGE_SIZE)),
  );
  protected previous = computed(() => {
    this.result(); // re-read once the page has landed, when `visited` has caught up
    const page = this.page();
    if (page <= 1) return null;
    const cursor = page > 2 ? this.visited.get(this.visitKey(page - 1)) : undefined;
    return cursor
      ? { first: false, query: { page: page - 1, cursor } }
      : { first: page > 2, query: { page: null, cursor: null } };
  });
  /** Product shots are lit on white and go flat under the hero veil; the shop floor doesn't. */
  protected shopPhotos = SHOP_PHOTOS;

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const c = CATEGORIES.find((x) => x.slug === this.cat());
      const label = c?.slug ? c.label : 'Local Art';
      const later =
        this.page() > 1
          ? `page=${this.page()}&cursor=${encodeURIComponent(this.cursor() ?? '')}`
          : '';
      seo.set({
        title: `Shop ${label} by Chicago Artists`,
        description: `Buy original ${label.toLowerCase()} made by North Side Chicago artists. Prints, jewelry, ceramics and paintings from the 6311arts gallery shop in Edgewater.`,
        path:
          '/shop' +
          (c?.slug ? `?category=${c.slug}` : '') +
          (later ? `${c?.slug ? '&' : '?'}${later}` : ''),
        breadcrumbs: [{ name: 'Shop', path: '/shop' }],
      });
    });
  }

  setSort(v: string) {
    this.go({ sort: v || null, page: null, cursor: null });
  }

  /** Set by a pager link, so the next page's arrival moves the visitor to it. */
  private turning = false;
  private count = viewChild<ElementRef<HTMLElement>>('count');
  private injector = inject(Injector);

  /**
   * A filter or the order: the list changes in place, and the visitor stays where they are rather than being thrown
   * back to the hero, which on a phone is a screen and a half away. The links keep real hrefs for crawlers, new tabs
   * and middle clicks; only a plain click is taken over.
   */
  protected stay(event: MouseEvent, queryParams: Record<string, string | number | null>): void {
    if (!plainClick(event)) return;
    event.preventDefault();
    this.go(queryParams);
  }

  /** Previous or Next: the same in-place change, then the visitor is taken to the top of the new page's works. */
  protected turn(event: MouseEvent, queryParams: Record<string, string | number | null>): void {
    if (!plainClick(event)) return;
    event.preventDefault();
    this.turning = true;
    this.go(queryParams);
  }

  /**
   * The address a filter or page link stands for. Real hrefs (not `routerLink`, which would navigate a second time
   * and scroll to the top) so the links still work for crawlers, new tabs and middle clicks.
   */
  protected href(queryParams: Record<string, string | number | null>): string {
    return this.router.serializeUrl(
      this.router.createUrlTree([], { relativeTo: this.route, queryParams, queryParamsHandling: 'merge' }),
    );
  }

  private go(queryParams: Record<string, string | number | null>): void {
    this.router.navigate([], { queryParams, queryParamsHandling: 'merge', scroll: 'manual', info: IN_PAGE });
  }

  /**
   * Once the new page has rendered: focus the count, so a keyboard or screen-reader user carries on from the new
   * works rather than from a Next button that may be gone, and scroll it under the fixed bar (its scroll-margin).
   */
  private landOnResults(): void {
    this.turning = false;
    afterNextRender(
      () => {
        const count = this.count()?.nativeElement;
        if (!count) return;
        count.focus({ preventScroll: true });
        count.scrollIntoView({ block: 'start' });
      },
      { injector: this.injector },
    );
  }
}

/** A click the browser would follow as a link: not a new tab, window or download. */
function plainClick(event: MouseEvent): boolean {
  return event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey;
}

@Component({
  selector: 'app-product-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ArtImage, ProductCard, MoneyPipe, RouterLink, UiIcon, NotFound],
  template: `
    @if (product(); as p) {
      <section class="band band--white band--after-nav" id="content">
        <div class="container detail">
          <div class="detail__media">
            <app-art-image [src]="p.image" [alt]="p.name" ratio="4 / 5" [priority]="true" />
          </div>
          <div class="detail__info">
            <nav class="crumbs" aria-label="Breadcrumb">
              <a routerLink="/shop">Shop</a> /
              @if (p.artist?.slug) {
                <a [routerLink]="['/artists', p.artist?.slug]">{{ p.artist?.name }}</a>
              } @else if (p.artist) {
                <span>{{ p.artist.name }}</span>
              }
            </nav>
            <h1 class="detail__title">{{ p.name }}</h1>
            @if (p.artist?.slug) {
              <p class="detail__by">
                by <a [routerLink]="['/artists', p.artist?.slug]">{{ p.artist?.name }}</a>
              </p>
            } @else if (p.artist) {
              <p class="detail__by">by {{ p.artist.name }}</p>
            }
            @if (p.description) {
              <p class="detail__lead">{{ p.description }}</p>
            }
            <div class="buy">
              @if (p.price != null) {
                <span class="buy__price">{{ p.price | money }}</span>
              } @else {
                <!-- Square hasn't priced the piece yet (ADR 0042, 0046). -->
                <p class="buy__note buy__tba">Price to be announced</p>
              }
              @if (!p.inStock) {
                <span class="tag tag--sold">Sold</span>
              } @else if (p.price != null) {
                <!-- Until Square has priced a piece it isn't for sale (ADR 0042). -->
                <a class="btn btn-dark btn-lg" [href]="store.buyUrl(p)">Buy now</a>
              }
            </div>
            <ul class="facts facts--small">
              <li>
                <ui-icon name="pin" [size]="20" /><span
                  >See it in person at {{ site.address.street }} during gallery hours</span
                >
              </li>
              <li>
                <ui-icon name="heart" [size]="20" /><span
                  >Made by a North Side artist within six miles of the gallery</span
                >
              </li>
              <li>
                <ui-icon name="gift" [size]="20" /><span
                  >Returns within 14 days, undamaged, with receipt</span
                >
              </li>
            </ul>
          </div>
        </div>
      </section>
      @if (more().length) {
        <section class="band band--light">
          <div class="container">
            <h2 class="band__title text-center">
              More from {{ p.artist?.slug ? p.artist?.name : 'the shop' }}
            </h2>
            <div class="grid-products">
              @for (m of more(); track m.id) {
                <app-product-card [item]="m" />
              }
            </div>
          </div>
        </section>
      }
    } @else if (loaded()) {
      <app-not-found />
    }
  `,
})
export class ProductDetail {
  private api = inject(SiteApi);
  protected store = inject(LegacyStore);
  protected site = SITE;
  private route = inject(ActivatedRoute);
  private data = toSignal(
    this.route.paramMap.pipe(
      switchMap((p) => this.api.product(p.get('slug')!).pipe(map((product) => ({ product })))),
    ),
  );
  protected loaded = computed(() => this.data() !== undefined);
  protected product = computed(() => this.data()?.product);
  protected more = toSignal(
    this.route.paramMap.pipe(
      switchMap((p) => this.api.product(p.get('slug')!)),
      switchMap((p) => this.api.products({ artist: p?.artist?.slug, limit: 5 })),
      map((r) => r.items.filter((i) => i.id !== this.product()?.id).slice(0, 4)),
    ),
    { initialValue: [] },
  );

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const p = this.product();
      if (!p) return;
      const path = `/shop/${p.slug}`;
      seo.set({
        title: `${p.name}${p.artist ? ` by ${p.artist.name}` : ''}`,
        description:
          p.description ||
          `${p.name}${p.artist ? ` by ${p.artist.name}` : ''}. Original work by a North Side Chicago artist, available at 6311arts in Edgewater.`,
        path,
        image: p.image,
        type: 'product',
        breadcrumbs: [
          { name: 'Shop', path: '/shop' },
          ...(p.artist?.slug ? [{ name: p.artist.name, path: `/artists/${p.artist.slug}` }] : []),
          { name: p.name, path },
        ],
        jsonLd: [productLd(p, SITE.url + path)],
      });
    });
  }
}
