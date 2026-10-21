import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { SHOP_PHOTOS } from '../../data/venue-photos';
import { ProductCard } from '../../shared/cards/product-card/product-card';
import { Hero } from '../../shared/hero/hero';

@Component({
  selector: 'app-artist-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ProductCard, RouterLink],
  templateUrl: './artist-detail.html',
})
export class ArtistDetail {
  private api = inject(SiteApi);
  private slug = toSignal(inject(ActivatedRoute).paramMap.pipe(map((p) => p.get('slug')!)), {
    requireSync: true,
  });
  private artists = toSignal(this.api.artists(), { initialValue: [] });
  protected artist = computed(() => this.artists().find((a) => a.slug === this.slug()));
  protected nameFromSlug = computed(() =>
    this.slug()
      .replace(/-/g, ' ')
      .replace(/\b\w/g, (c) => c.toUpperCase()),
  );
  protected works = toSignal(
    inject(ActivatedRoute).paramMap.pipe(
      switchMap((p) => this.api.products({ artist: p.get('slug')!, limit: 24 })),
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
        breadcrumbs: [
          { name: 'Artists', path: '/artists' },
          { name, path },
        ],
        jsonLd: [
          {
            '@type': 'Person',
            name,
            url: SITE.url + path,
            affiliation: { '@id': `${SITE.url}/#org` },
          },
        ],
      });
    });
  }
}
