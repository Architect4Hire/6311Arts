import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { Seo, exhibitionLd } from '../core/seo.service';
import { WpApi } from '../core/wp-api.service';
import { GALLERY_PHOTOS } from '../data/venue-photos';
import { ArtImage } from '../shared/art-image';
import { ExhibitionCard, exhibitionStatus } from '../shared/cards';
import { WhenPipe } from '../shared/format';
import { Hero } from '../shared/hero';
import { Reveal } from '../shared/reveal';

@Component({
  selector: 'app-exhibitions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ExhibitionCard, ArtImage, UiIcon, Reveal, RouterLink, WhenPipe],
  template: `
    <app-hero title="Exhibitions" eyebrow="Free · Open Wed – Sun"
      lead="A new show about every six weeks across the main, East and West galleries, from solo shows to work by students from the high schools nearby."
      [images]="galleryPhotos" />

    @for (e of spotlight(); track e.slug; let odd = $odd) {
      <section class="band" [class.band--dark]="!odd" [class.band--light]="odd" [attr.id]="$first ? 'content' : null">
        <div class="container spotlight" [class.spotlight--flip]="odd">
          <app-art-image [src]="e.image" [alt]="e.title" ratio="4 / 5" [priority]="$first" reveal />
          <div class="spotlight__text" reveal="150">
            <p class="eyebrow" [class.eyebrow--dark]="odd">{{ status(e) }} · {{ e.start | when: 'range' : e.end }}</p>
            <h2 class="band__title band__title--xl band__title--left">{{ e.title }}</h2>
            <p class="band__lead band__lead--left">{{ e.summary }}</p>
            @if (e.artists.length) {
              <p class="artists-inline">{{ e.artists.join(' · ') }}</p>
            }
            @if (e.reception) {
              <p class="band__note band__note--left"><ui-icon name="ticket" [size]="20" />
                Opening reception {{ e.reception.start | when: 'long' }}, {{ e.reception.start | when: 'time' : e.reception.end }}. {{ e.reception.note }}.</p>
            }
            <a class="btn btn-lg" [class.btn-white]="!odd" [class.btn-dark]="odd" routerLink="/visit">Plan your visit</a>
          </div>
        </div>
      </section>
    }

    <section class="band band--white">
      <div class="container">
        <h2 class="band__title text-center">Past exhibitions</h2>
        <div class="grid-exhibits">
          @for (e of past(); track e.slug; let i = $index) {
            <app-exhibition-card [item]="e" [reveal]="(i % 3) * 120" />
          }
        </div>
      </div>
    </section>

    <section class="band band--dark text-center">
      <div class="container container--mid" reveal>
        <h2 class="band__title">Show your work here</h2>
        <p class="band__lead">We’re always looking for North Side artists for solo, group and window shows.</p>
        <a class="btn btn-white btn-lg" routerLink="/submit">Submit your work</a>
      </div>
    </section>
  `,
})
export class Exhibitions {
  private all = toSignal(inject(WpApi).exhibitions(), { initialValue: [] });
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
