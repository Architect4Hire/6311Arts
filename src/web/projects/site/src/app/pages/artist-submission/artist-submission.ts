import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { Hero } from '../../shared/hero/hero';
import { Reveal } from '../../shared/reveal';
import { STUDIO_PHOTOS } from '../../data/venue-photos';
import { FormsApi, SUBMISSION_LIMITS, SubmissionAnswers } from '../../services/forms-api.service';

/** What an applicant can ask us for. The three panels below open from these. */
const GOALS = [
  { id: 'sell', label: 'Sell in the shop' },
  { id: 'exhibit', label: 'Show in the gallery' },
  { id: 'teach', label: 'Teach a class' },
  { id: 'hello', label: 'Just saying hello' },
] as const;
type Goal = (typeof GOALS)[number]['id'];

const MEDIUMS = [
  'Painting',
  'Printmaking',
  'Drawing',
  'Photography',
  'Ceramics',
  'Jewelry',
  'Sculpture',
  'Fiber & textile',
  'Collage',
  'Mixed media',
  'Other',
];

const PRICE_BANDS = ['Under $100', '$100 – $300', '$300 – $750', '$750 – $2,000', 'Over $2,000'];

const FORMATS = ['One-night workshop', 'Three-week course', 'Five-week course', 'Not sure yet'];

const WINDOWS = [
  'Next 3 months',
  '3 – 6 months out',
  '6 – 12 months out',
  'Whenever you have room',
];

const HEARD = [
  'Walked past the gallery',
  'Another artist',
  'A class or workshop',
  'Instagram',
  'Search',
  'Somewhere else',
];

/**
 * The artist submission form, which replaced the Google Form linked from /submit (prompt 3.3). It posts to
 * `/api/v1/forms/submissions` (ADR 0047): the answers as JSON, then up to six pictures; staff get a notification and
 * the artist a short confirmation. Every control is template-driven and bound to a signal.
 */
@Component({
  selector: 'app-artist-submission',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Hero, UiIcon, Reveal, RouterLink],
  templateUrl: './artist-submission.html',
})
export class ArtistSubmission {
  protected site = SITE;
  private forms = inject(FormsApi);
  protected studioPhotos = STUDIO_PHOTOS;
  protected goals = GOALS;
  protected mediums = MEDIUMS;
  protected priceBands = PRICE_BANDS;
  protected formats = FORMATS;
  protected windows = WINDOWS;
  protected heardOptions = HEARD;

  // 1 · about you
  protected name = signal('');
  protected pronouns = signal('');
  protected email = signal('');
  protected phone = signal('');
  protected neighborhood = signal('');
  protected zip = signal('');
  protected website = signal('');
  protected instagram = signal('');
  // 2 · the work
  protected medium = signal('');
  protected otherMedia = signal('');
  protected years = signal<number | null>(null);
  protected priceBand = signal('');
  protected bio = signal('');
  protected statement = signal('');
  // 3 · what for
  protected wants = signal<Goal[]>([]);
  protected piecesReady = signal<number | null>(null);
  protected restock = signal('');
  protected showTitle = signal('');
  protected workCount = signal<number | null>(null);
  protected largest = signal('');
  protected showWindow = signal('');
  protected gallery = signal('');
  protected classTitle = signal('');
  protected maxStudents = signal<number | null>(null);
  protected format = signal('');
  protected materials = signal('');
  protected taughtBefore = signal(false);
  // 4 · images
  protected files = signal<File[]>([]);
  protected portfolio = signal('');
  // 5 · the rest
  protected heard = signal('');
  protected ownWork = signal(false);
  protected newsletter = signal(true);
  protected trap = signal('');

  protected sent = signal(false);
  protected sending = signal(false);
  protected error = signal('');

  protected has(g: Goal): boolean {
    return this.wants().includes(g);
  }

  protected toggle(g: Goal): void {
    this.wants.update((w) => (w.includes(g) ? w.filter((x) => x !== g) : [...w, g]));
  }

  protected pickFiles(e: Event): void {
    const picked = (e.target as HTMLInputElement).files;
    this.files.set(picked ? [...picked] : []);
  }

  protected kilobytes(file: File): number {
    return Math.round(file.size / 1024);
  }

  /** The answers in the shape the API reads; a goal's panel goes only when the goal is picked. */
  private answers(): SubmissionAnswers {
    return {
      artist: {
        name: this.name().trim(),
        email: this.email().trim(),
        pronouns: this.pronouns(),
        phone: this.phone(),
        neighborhood: this.neighborhood(),
        zip: this.zip(),
        website: this.website(),
        instagram: this.instagram(),
      },
      work: {
        medium: this.medium(),
        otherMedia: this.otherMedia(),
        years: this.years(),
        priceBand: this.priceBand(),
        bio: this.bio(),
        statement: this.statement(),
      },
      wants: this.wants(),
      sell: this.has('sell')
        ? { piecesReady: this.piecesReady(), restock: this.restock() }
        : undefined,
      exhibit: this.has('exhibit')
        ? {
            showTitle: this.showTitle(),
            workCount: this.workCount(),
            largest: this.largest(),
            window: this.showWindow(),
            gallery: this.gallery(),
          }
        : undefined,
      teach: this.has('teach')
        ? {
            classTitle: this.classTitle(),
            maxStudents: this.maxStudents(),
            format: this.format(),
            materials: this.materials(),
            taughtBefore: this.taughtBefore(),
          }
        : undefined,
      portfolio: this.portfolio(),
      heard: this.heard(),
      consent: { ownWork: this.ownWork(), newsletter: this.newsletter() },
      company: this.trap(),
    };
  }

  protected async send(): Promise<void> {
    if (!this.name().trim()) return this.error.set('Please tell us your name.');
    if (!/^\S+@\S+\.\S+$/.test(this.email()))
      return this.error.set('That email address does not look right.');
    if (!this.wants().length) return this.error.set('Pick at least one thing you are here for.');
    if (!this.ownWork()) return this.error.set('Please confirm the work is yours.');
    if (this.files().length > SUBMISSION_LIMITS.maxImages) {
      return this.error.set(`Choose up to ${SUBMISSION_LIMITS.maxImages} images.`);
    }
    const tooLarge = this.files().find((file) => file.size > SUBMISSION_LIMITS.maxImageBytes);
    if (tooLarge)
      return this.error.set(`${tooLarge.name} is larger than 10 MB. Choose a smaller copy.`);

    this.error.set('');
    this.sending.set(true);
    const outcome = await this.forms.submit(this.answers(), this.files());
    this.sending.set(false);
    if (outcome.ok) return this.sent.set(true);
    this.error.set(
      outcome.kind === 'invalid'
        ? outcome.message
        : outcome.kind === 'busy'
          ? 'We have had a lot of submissions from your connection. Please try again in an hour.'
          : `That didn't go through. Please try again, or email ${this.site.email}.`,
    );
  }

  constructor() {
    inject(Seo).set({
      title: 'Artist Submission Form | Teach, Exhibit or Sell at 6311arts',
      description:
        'Submit your work to 6311arts: one form for selling in the shop, showing in the galleries and teaching a class on Chicago’s North Side.',
      path: '/artists/submit',
      noindex: true,
      breadcrumbs: [
        { name: 'Artists', path: '/artists' },
        { name: 'Submit your work', path: '/artists/submit' },
      ],
    });
  }
}
