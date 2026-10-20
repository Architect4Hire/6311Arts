import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Seo, productLd } from '../core/seo.service';
import { SITE } from '../core/site';
import { WpApi } from '../core/wp-api.service';
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

/** Shop grid. Category, sort and page live in the query string so every view is a crawlable, shareable URL. */
@Component({
  selector: 'app-shop',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ProductCard, RouterLink, UiIcon],
  template: `
    <app-hero title="Shop local art" eyebrow="780+ original works"
      lead="Every piece is made by an artist who lives within six miles of the gallery. Buy online or see it in person on Clark Street." [images]="shopPhotos" />
    <section class="band band--white" id="content">
      <div class="container">
        <div class="filters">
          @for (c of cats; track c.slug) {
            <a class="chip" [class.is-on]="cat() === c.slug" [routerLink]="[]" [queryParams]="{ category: c.slug || null, page: null }" queryParamsHandling="merge">{{ c.label }}</a>
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
        <p class="filters__count">{{ result()?.total ?? 0 }} works</p>
        <div class="grid-products">
          @for (p of result()?.items ?? []; track p.id) {
            <app-product-card [item]="p" />
          }
        </div>
        @if ((result()?.totalPages ?? 1) > 1) {
          <nav class="pager" aria-label="Pages">
            @if (page() > 1) {
              <a class="btn btn-border-dark" [routerLink]="[]" [queryParams]="{ page: page() - 1 === 1 ? null : page() - 1 }" queryParamsHandling="merge" rel="prev">Previous</a>
            }
            <span>Page {{ page() }} of {{ result()?.totalPages }}</span>
            @if (page() < (result()?.totalPages ?? 1)) {
              <a class="btn btn-dark" [routerLink]="[]" [queryParams]="{ page: page() + 1 }" queryParamsHandling="merge" rel="next">Next</a>
            }
          </nav>
        }
      </div>
    </section>
    <section class="band band--light">
      <div class="container cols-3 text-center">
        <div class="feature feature--dark"><ui-icon name="heart" [size]="56" [stroke]="1" /><h3 class="feature__title">Support artists</h3><p>Every sale supports the local artist who made the work.</p></div>
        <div class="feature feature--dark"><ui-icon name="pin" [size]="56" [stroke]="1" /><h3 class="feature__title">See it in person</h3><p>Most pieces are on display in the gallery, Wednesday to Sunday.</p></div>
        <div class="feature feature--dark"><ui-icon name="gift" [size]="56" [stroke]="1" /><h3 class="feature__title">14-day returns</h3><p>Shop items come back undamaged with a receipt for a full refund.</p></div>
      </div>
    </section>
  `,
})
export class Shop {
  private api = inject(WpApi);
  private router = inject(Router);
  private qp = toSignal(inject(ActivatedRoute).queryParamMap, { requireSync: true });
  protected cats = CATEGORIES;
  protected cat = computed(() => this.qp().get('category') ?? '');
  protected sort = computed(() => this.qp().get('sort') ?? '');
  protected page = computed(() => Math.max(1, Number(this.qp().get('page') ?? 1)));

  protected result = toSignal(
    inject(ActivatedRoute).queryParamMap.pipe(
      switchMap((q) => {
        const sort = q.get('sort') ?? '';
        return this.api.products({
          category: q.get('category') ?? undefined,
          page: Number(q.get('page') ?? 1),
          perPage: 12,
          orderby: sort.startsWith('price') ? 'price' : sort === 'title' ? 'title' : undefined,
          order: sort === 'price-asc' || sort === 'title' ? 'asc' : 'desc',
        });
      }),
    ),
  );
  /** Product shots are lit on white and go flat under the hero veil; the shop floor doesn't. */
  protected shopPhotos = SHOP_PHOTOS;

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const c = CATEGORIES.find((x) => x.slug === this.cat());
      const label = c?.slug ? c.label : 'Local Art';
      seo.set({
        title: `Shop ${label} by Chicago Artists`,
        description: `Buy original ${label.toLowerCase()} made by North Side Chicago artists. Prints, jewelry, ceramics and paintings from the 6311arts gallery shop in Edgewater.`,
        path: '/shop' + (c?.slug ? `?category=${c.slug}` : '') + (this.page() > 1 ? `${c?.slug ? '&' : '?'}page=${this.page()}` : ''),
        breadcrumbs: [{ name: 'Shop', path: '/shop' }],
      });
    });
  }

  setSort(v: string) {
    this.router.navigate([], { queryParams: { sort: v || null, page: null }, queryParamsHandling: 'merge' });
  }
}

@Component({
  selector: 'app-product-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ArtImage, ProductCard, MoneyPipe, RouterLink, UiIcon, NotFound],
  template: `
    @if (product(); as p) {
      <section class="band band--white band--after-nav" id="content">
        <div class="container detail">
          <div class="detail__media"><app-art-image [src]="p.image" [alt]="p.name" ratio="4 / 5" [priority]="true" /></div>
          <div class="detail__info">
            <nav class="crumbs" aria-label="Breadcrumb"><a routerLink="/shop">Shop</a> /
              @if (p.artist) { <a [routerLink]="['/artists', p.artist.slug]">{{ p.artist.name }}</a> }</nav>
            <h1 class="detail__title">{{ p.name }}</h1>
            @if (p.artist) {
              <p class="detail__by">by <a [routerLink]="['/artists', p.artist.slug]">{{ p.artist.name }}</a></p>
            }
            @if (p.description) { <p class="detail__lead">{{ p.description }}</p> }
            <div class="buy">
              <span class="buy__price">{{ p.price | money }}</span>
              @if (p.inStock) {
                <a class="btn btn-dark btn-lg" [href]="api.buyUrl(p)">Buy now</a>
              } @else {
                <span class="tag tag--sold">Sold</span>
              }
            </div>
            <ul class="facts facts--small">
              <li><ui-icon name="pin" [size]="20" /><span>See it in person at {{ site.address.street }} during gallery hours</span></li>
              <li><ui-icon name="heart" [size]="20" /><span>Made by a North Side artist within six miles of the gallery</span></li>
              <li><ui-icon name="gift" [size]="20" /><span>Returns within 14 days, undamaged, with receipt</span></li>
            </ul>
          </div>
        </div>
      </section>
      @if (more().length) {
        <section class="band band--light">
          <div class="container">
            <h2 class="band__title text-center">More from {{ p.artist?.name ?? 'the shop' }}</h2>
            <div class="grid-products">
              @for (m of more(); track m.id) { <app-product-card [item]="m" /> }
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
  protected api = inject(WpApi);
  protected site = SITE;
  private route = inject(ActivatedRoute);
  private data = toSignal(this.route.paramMap.pipe(switchMap((p) => this.api.product(p.get('slug')!).pipe(map((product) => ({ product }))))));
  protected loaded = computed(() => this.data() !== undefined);
  protected product = computed(() => this.data()?.product);
  protected more = toSignal(
    this.route.paramMap.pipe(
      switchMap((p) => this.api.product(p.get('slug')!)),
      switchMap((p) => this.api.products({ artist: p?.artist?.slug, perPage: 5 })),
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
        description: p.description || `${p.name}${p.artist ? ` by ${p.artist.name}` : ''}. Original work by a North Side Chicago artist, available at 6311arts in Edgewater.`,
        path,
        image: p.image,
        type: 'product',
        breadcrumbs: [{ name: 'Shop', path: '/shop' }, ...(p.artist ? [{ name: p.artist.name, path: `/artists/${p.artist.slug}` }] : []), { name: p.name, path }],
        jsonLd: [productLd(p, SITE.url + path)],
      });
    });
  }
}
