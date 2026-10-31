import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { LegacyStore } from '../../core/legacy-store';
import { Seo, productLd } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { ArtImage } from '../../shared/media/art-image/art-image';
import { ProductCard } from '../../shared/cards/product-card/product-card';
import { MoneyPipe } from '../../shared/format';
import { NotFound } from '../not-found/not-found';

@Component({
  selector: 'app-product-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ArtImage, ProductCard, MoneyPipe, RouterLink, UiIcon, NotFound],
  templateUrl: './product-detail.html',
})
export class ProductDetail {
  private api = inject(SiteApi);
  protected store = inject(LegacyStore);
  protected site = SITE;
  /** The route's `:slug`, bound by the router (`withComponentInputBinding`), or the preview page's (ADR 0063). */
  readonly slug = input.required<string>();

  private data = toSignal(
    toObservable(this.slug).pipe(
      switchMap((slug) => this.api.product(slug).pipe(map((product) => ({ product })))),
    ),
  );
  protected loaded = computed(() => this.data() !== undefined);
  protected product = computed(() => this.data()?.product);
  protected more = toSignal(
    toObservable(this.slug).pipe(
      switchMap((slug) => this.api.product(slug)),
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
