import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Seo, exhibitionLd } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { GALLERY_PHOTOS } from '../../data/venue-photos';
import { ArtImage } from '../../shared/media/art-image/art-image';
import {
  ExhibitionCard,
  exhibitionStatus,
} from '../../shared/cards/exhibition-card/exhibition-card';
import { WhenPipe } from '../../shared/format';
import { Hero } from '../../shared/hero/hero';
import { NotFound } from '../not-found/not-found';

/**
 * One show: its poster, when it is on, who is in it, the opening reception, and the write-up staff gave it
 * (ADR 0064). The exhibitions page lists every show and links here; the sitemap has always named this address.
 *
 * A credited artist is linked when they are published and named in plain text when they are not, which is how
 * every other credit on the site reads (ADR 0042).
 */
@Component({
  selector: 'app-exhibition-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ArtImage, ExhibitionCard, UiIcon, RouterLink, WhenPipe, NotFound],
  templateUrl: './exhibition-detail.html',
})
export class ExhibitionDetail {
  /** The route's `:slug`, bound by the router (`withComponentInputBinding`), or the preview page's (ADR 0063). */
  readonly slug = input.required<string>();

  private api = inject(SiteApi);
  protected site = SITE;
  /** The banner shows the rooms; the show's own poster is in the band below it. */
  protected galleryPhotos = GALLERY_PHOTOS;

  private data = toSignal(
    toObservable(this.slug).pipe(
      switchMap((slug) => this.api.exhibition(slug).pipe(map((show) => ({ show })))),
    ),
  );
  protected loaded = computed(() => this.data() !== undefined);
  protected show = computed(() => this.data()?.show ?? undefined);
  /** The read failed rather than came back empty: the server is answering 503, so this is not a 404 (ADR 0064). */
  protected unavailable = computed(() => this.data()?.show === null);
  protected status = computed(() => {
    const show = this.show();
    return show ? exhibitionStatus(show) : null;
  });

  private all = toSignal(this.api.exhibitions(), { initialValue: [] });
  /** The three nearest other shows, soonest first, so a visitor has somewhere to go next. */
  protected more = computed(() =>
    this.all()
      .filter((e) => e.slug !== this.slug())
      .slice(0, 3),
  );

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const show = this.show();
      if (!show) return;
      const path = `/exhibitions/${show.slug}`;
      seo.set({
        title: `${show.title} | Exhibition in Edgewater, Chicago`,
        description:
          show.summary ||
          show.body?.[0] ||
          `${show.title}, a free exhibition at 6311arts on Clark Street in Edgewater, Chicago.`,
        path,
        image: show.image,
        type: 'event',
        breadcrumbs: [
          { name: 'Exhibitions', path: '/exhibitions' },
          { name: show.title, path },
        ],
        jsonLd: [exhibitionLd(show)],
      });
    });
  }
}
