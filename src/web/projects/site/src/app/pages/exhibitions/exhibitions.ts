import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { Seo, exhibitionLd } from '../../core/seo.service';
import { SiteApi } from '../../services/site-api.service';
import { GALLERY_PHOTOS } from '../../data/venue-photos';
import { ArtImage } from '../../shared/media/art-image/art-image';
import {
  ExhibitionCard,
  exhibitionStatus,
} from '../../shared/cards/exhibition-card/exhibition-card';
import { WhenPipe } from '../../shared/format';
import { Hero } from '../../shared/hero/hero';
import { Reveal } from '../../shared/reveal';

@Component({
  selector: 'app-exhibitions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ExhibitionCard, ArtImage, UiIcon, Reveal, RouterLink, WhenPipe],
  templateUrl: './exhibitions.html',
  styleUrl: './exhibitions.scss',
})
export class Exhibitions {
  private all = toSignal(inject(SiteApi).exhibitions(), { initialValue: [] });
  protected spotlight = computed(() =>
    this.all()
      .filter((e) => exhibitionStatus(e) !== 'Past')
      .sort((a, b) => a.start.localeCompare(b.start)),
  );
  protected past = computed(() => this.all().filter((e) => exhibitionStatus(e) === 'Past'));
  /** The banner shows the rooms; each show's own poster is the spotlight image below it. */
  protected galleryPhotos = GALLERY_PHOTOS;
  protected status = exhibitionStatus;

  constructor() {
    const seo = inject(Seo);
    effect(() =>
      seo.set({
        title: 'Exhibitions: Current & Upcoming Art Shows in Edgewater, Chicago',
        description:
          'Free art exhibitions at 6311arts on Clark Street: current and upcoming shows by North Side artists, opening receptions, and the full archive.',
        path: '/exhibitions',
        breadcrumbs: [{ name: 'Exhibitions', path: '/exhibitions' }],
        jsonLd: this.spotlight().map(exhibitionLd),
      }),
    );
  }
}
