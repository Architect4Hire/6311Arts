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
import { ActivatedRoute, Router } from '@angular/router';
import { switchMap, tap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { IN_PAGE } from '../../core/navigation';
import { Seo } from '../../core/seo.service';
import { ShopSort, SiteApi } from '../../services/site-api.service';
import { SHOP_PHOTOS } from '../../data/venue-photos';
import { ProductCard } from '../../shared/cards/product-card/product-card';
import { Hero } from '../../shared/hero/hero';

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
  templateUrl: './shop.html',
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
      this.router.createUrlTree([], {
        relativeTo: this.route,
        queryParams,
        queryParamsHandling: 'merge',
      }),
    );
  }

  private go(queryParams: Record<string, string | number | null>): void {
    this.router.navigate([], {
      queryParams,
      queryParamsHandling: 'merge',
      scroll: 'manual',
      info: IN_PAGE,
    });
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
