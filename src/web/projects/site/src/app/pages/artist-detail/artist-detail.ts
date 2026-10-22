import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  linkedSignal,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Product } from '../../core/models';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { SHOP_PHOTOS } from '../../data/venue-photos';
import { ArtImage } from '../../shared/media/art-image/art-image';
import { ProductCard } from '../../shared/cards/product-card/product-card';
import { Hero } from '../../shared/hero/hero';
import { NotFound } from '../not-found/not-found';

/**
 * One artist as staff wrote them up in the CMS — portrait, medium, bio, whether they show here now — and the work of
 * theirs the shop has for sale. One read (`/site/artists/{slug}`) carries both; further work pages in from the shop
 * list when asked for. An artist who also teaches links to their instructor page, which links back here.
 */
@Component({
  selector: 'app-artist-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ArtImage, ProductCard, UiIcon, RouterLink, NotFound],
  templateUrl: './artist-detail.html',
})
export class ArtistDetail {
  private api = inject(SiteApi);
  private route = inject(ActivatedRoute);
  protected site = SITE;
  /** The artist's own work fills the grid below; the banner shows the shop it sells in. */
  protected shopPhotos = SHOP_PHOTOS;

  private slug = toSignal(this.route.paramMap.pipe(map((p) => p.get('slug')!)), {
    requireSync: true,
  });
  private data = toSignal(
    this.route.paramMap.pipe(
      switchMap((p) => this.api.artist(p.get('slug')!).pipe(map((page) => ({ page })))),
    ),
  );
  protected loaded = computed(() => this.data() !== undefined);
  protected artist = computed(() => this.data()?.page?.artist);
  protected initials = computed(() =>
    (this.artist()?.name ?? '')
      .split(' ')
      .map((w) => w[0])
      .join('')
      .slice(0, 3),
  );

  /** The work shown so far: the page's first page of it, then whatever "More work" has added. */
  protected works = linkedSignal<Product[]>(() => this.data()?.page?.works ?? []);
  protected nextCursor = linkedSignal(() => this.data()?.page?.nextCursor ?? null);
  protected loadingMore = signal(false);

  private instructors = toSignal(this.api.instructors(), { initialValue: [] });
  /** Their instructor profile, matched the way that page matches back here. */
  protected teaches = computed(() =>
    this.instructors().find((i) => (i.artistSlug ?? i.slug) === this.slug()),
  );

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const a = this.artist();
      if (!a) return;
      const path = `/artists/${a.slug}`;
      const count = this.works().length;
      seo.set({
        title: `${a.name} | Local Artist`,
        description:
          a.bio?.[0] ??
          `Shop original work by ${a.name}, a North Side Chicago artist showing at 6311arts in Edgewater.${count ? ` ${count} pieces available.` : ''}`,
        path,
        image: a.image ?? this.works()[0]?.image,
        breadcrumbs: [
          { name: 'Artists', path: '/artists' },
          { name: a.name, path },
        ],
        jsonLd: [
          {
            '@type': 'Person',
            name: a.name,
            description: a.bio?.join('\n\n'),
            image: a.image,
            url: SITE.url + path,
            affiliation: { '@id': `${SITE.url}/#org` },
          },
        ],
      });
    });
  }

  /** The next page of their work from the shop list, appended under what is shown; dropped if the visitor moved on. */
  protected loadMore(): void {
    const slug = this.slug();
    const cursor = this.nextCursor();
    if (!cursor || this.loadingMore()) return;
    this.loadingMore.set(true);
    this.api.products({ artist: slug, cursor }).subscribe((page) => {
      this.loadingMore.set(false);
      if (slug !== this.slug()) return;
      this.works.update((shown) => [...shown, ...page.items]);
      this.nextCursor.set(page.nextCursor);
    });
  }
}
