import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { Seo } from '../core/seo.service';
import { SITE } from '../core/site';
import { WpApi } from '../core/wp-api.service';
import { SHOP_PHOTOS } from '../data/venue-photos';
import { ProductCard } from '../shared/cards';
import { Hero } from '../shared/hero';
import { Reveal } from '../shared/reveal';

@Component({
  selector: 'app-artists',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, RouterLink, Reveal],
  template: `
    <app-hero title="Our artists" eyebrow="All within six miles"
      lead="Painters, printmakers, jewelers, photographers and sculptors from Edgewater, Rogers Park and the rest of the North Side." />
    <section class="band band--white" id="content">
      <div class="container">
        <div class="filters">
          <label class="search">
            <span class="sr-only">Find an artist</span>
            <input type="search" placeholder="Find an artist…" [value]="q()" (input)="q.set($any($event.target).value)" />
          </label>
          <span class="filters__count">{{ filtered().length }} artists</span>
        </div>
        @for (g of letters(); track g.letter) {
          <div class="az" reveal>
            <h2 class="az__letter">{{ g.letter }}</h2>
            <ul class="az__list">
              @for (a of g.items; track a.slug) {
                <li>
                  <a [routerLink]="['/artists', a.slug]">
                    <span class="az__name">{{ a.name }}</span>
                    @if (a.medium) { <span class="az__medium">{{ a.medium }}</span> }
                    @if (a.productCount) { <span class="az__count">{{ a.productCount }} works</span> }
                  </a>
                </li>
              }
            </ul>
          </div>
        }
      </div>
    </section>
    <section class="band band--dark text-center">
      <div class="container container--mid" reveal>
        <h2 class="band__title">Live on the North Side? Join them.</h2>
        <p class="band__lead">Teach a class, show in the gallery or sell in the shop.</p>
        <a class="btn btn-white btn-lg" routerLink="/artists/submit">Submit your work</a>
      </div>
    </section>
  `,
})
export class Artists {
  private all = toSignal(inject(WpApi).artists(), { initialValue: [] });
  protected q = signal('');
  protected filtered = computed(() => {
    const q = this.q().trim().toLowerCase();
    return this.all().filter((a) => !q || a.name.toLowerCase().includes(q));
  });
  protected letters = computed(() => {
    const map = new Map<string, ReturnType<typeof this.filtered>>();
    for (const a of this.filtered()) {
      const l = /[a-z]/i.test(a.name[0]) ? a.name[0].toUpperCase() : '#';
      map.set(l, [...(map.get(l) ?? []), a]);
    }
    return [...map].sort(([a], [b]) => a.localeCompare(b)).map(([letter, items]) => ({ letter, items }));
  });

  constructor() {
    inject(Seo).set({
      title: 'Local Chicago Artists | North Side Painters, Printmakers & Makers',
      description:
        'Meet the 50+ artists who show and sell at 6311arts, all living within six miles of our Edgewater gallery. Browse their work and buy direct.',
      path: '/artists',
      breadcrumbs: [{ name: 'Artists', path: '/artists' }],
    });
  }
}

@Component({
  selector: 'app-artist-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ProductCard, RouterLink],
  template: `
    <app-hero [title]="artist()?.name ?? nameFromSlug()" [eyebrow]="artist()?.medium ?? '6311arts artist'"
      [images]="shopPhotos" />
    <section class="band band--white" id="content">
      <div class="container">
        @if (works().length) {
          <h2 class="band__title text-center">Available work</h2>
          <div class="grid-products">
            @for (p of works(); track p.id) { <app-product-card [item]="p" /> }
          </div>
        } @else {
          <p class="empty text-center">New work from {{ artist()?.name ?? nameFromSlug() }} is on its way. Stop by the gallery or
            <a routerLink="/shop">browse the whole shop</a>.</p>
        }
      </div>
    </section>
  `,
})
export class ArtistDetail {
  private api = inject(WpApi);
  private slug = toSignal(inject(ActivatedRoute).paramMap.pipe(map((p) => p.get('slug')!)), { requireSync: true });
  private artists = toSignal(this.api.artists(), { initialValue: [] });
  protected artist = computed(() => this.artists().find((a) => a.slug === this.slug()));
  protected nameFromSlug = computed(() => this.slug().replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase()));
  protected works = toSignal(
    inject(ActivatedRoute).paramMap.pipe(
      switchMap((p) => this.api.products({ artist: p.get('slug')!, perPage: 24 })),
      map((r) => r.items),
    ),
    { initialValue: [] },
  );
  /** The artist's own work fills the grid below; the banner shows the shop it sells in. */
  protected shopPhotos = SHOP_PHOTOS;

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const name = this.artist()?.name ?? this.nameFromSlug();
      const path = `/artists/${this.slug()}`;
      seo.set({
        title: `${name} | Local Artist`,
        description: `Shop original work by ${name}, a North Side Chicago artist showing at 6311arts in Edgewater.${this.works().length ? ` ${this.works().length} pieces available.` : ''}`,
        path,
        image: this.works()[0]?.image,
        breadcrumbs: [{ name: 'Artists', path: '/artists' }, { name, path }],
        jsonLd: [{ '@type': 'Person', name, url: SITE.url + path, affiliation: { '@id': `${SITE.url}/#org` } }],
      });
    });
  }
}
