import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Seo } from '../core/seo.service';
import { SITE } from '../core/site';
import { Hero } from '../shared/hero';
import { Icon } from '../shared/icon';
import { Reveal } from '../shared/reveal';
import { STUDIO_PHOTOS } from '../data/venue-photos';

/** What an applicant can ask us for. The three panels below open from these. */
const GOALS = [
  { id: 'sell', label: 'Sell in the shop' },
  { id: 'exhibit', label: 'Show in the gallery' },
  { id: 'teach', label: 'Teach a class' },
  { id: 'hello', label: 'Just saying hello' },
] as const;
type Goal = (typeof GOALS)[number]['id'];

const MEDIUMS = [
  'Painting', 'Printmaking', 'Drawing', 'Photography', 'Ceramics',
  'Jewelry', 'Sculpture', 'Fiber & textile', 'Collage', 'Mixed media', 'Other',
];

const PRICE_BANDS = ['Under $100', '$100 – $300', '$300 – $750', '$750 – $2,000', 'Over $2,000'];

const FORMATS = ['One-night workshop', 'Three-week course', 'Five-week course', 'Not sure yet'];

const WINDOWS = ['Next 3 months', '3 – 6 months out', '6 – 12 months out', 'Whenever you have room'];

const HEARD = ['Walked past the gallery', 'Another artist', 'A class or workshop', 'Instagram', 'Search', 'Somewhere else'];

/**
 * STUB. The artist submission form that replaces the Google Form linked from /submit.
 *
 * Nothing is sent anywhere yet: `send()` only collects what was typed and shows it back, so we can
 * agree on the fields before the API exists. Everything here is a plain template-driven control bound
 * to a signal, so the same markup can be dropped onto a real endpoint later.
 */
@Component({
  selector: 'app-artist-submission',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Hero, Icon, Reveal, RouterLink],
  template: `
    <app-hero title="Tell us about your work" eyebrow="Artist submission · about five minutes"
      lead="One form for all three doors: the shop, the galleries and the classroom. Have a few images and a short bio handy."
      [images]="studioPhotos" />

    <section class="band band--light" id="content">
      <div class="container container--mid">
        @if (sent()) {
          <div class="sform__done" reveal>
            <app-icon name="star" [size]="48" [stroke]="1" />
            <h2 class="band__title">Thanks, {{ name() || 'friend' }}.</h2>
            <p class="band__lead band__lead--left">
              Nothing was actually sent — this page is a stub while we settle on the questions. Here is everything it
              collected, which is what the real endpoint would receive.
            </p>
            <pre class="sform__payload">{{ payload() }}</pre>
            <p class="btn-row" style="justify-content: flex-start">
              <button class="btn btn-dark" type="button" (click)="sent.set(false)">Back to the form</button>
              <a class="btn btn-border-dark" routerLink="/artists">See who else shows here</a>
            </p>
          </div>
        } @else {
          <p class="band__note" style="margin-bottom: 28px">
            <app-icon name="spark" [size]="18" />
            Draft form — submissions are not received yet. For now, email {{ site.email }}.
          </p>

          <form class="sform" (ngSubmit)="send()" novalidate>
            <!-- 1 ---------------------------------------------------------------- -->
            <fieldset class="sform__step" reveal>
              <legend class="sform__legend"><span class="sform__num">1</span> About you</legend>
              <div class="sform__grid">
                <label class="field">
                  <span class="field__label">Full name <i>required</i></span>
                  <input name="name" [(ngModel)]="name" autocomplete="name" placeholder="Jane Rivera" />
                </label>
                <label class="field">
                  <span class="field__label">Pronouns</span>
                  <input name="pronouns" [(ngModel)]="pronouns" placeholder="she / they / he" />
                </label>
                <label class="field">
                  <span class="field__label">Email <i>required</i></span>
                  <input name="email" type="email" [(ngModel)]="email" autocomplete="email" placeholder="you@example.com" />
                </label>
                <label class="field">
                  <span class="field__label">Phone</span>
                  <input name="phone" type="tel" [(ngModel)]="phone" autocomplete="tel" placeholder="312-555-0134" />
                </label>
                <label class="field">
                  <span class="field__label">Neighborhood</span>
                  <input name="neighborhood" [(ngModel)]="neighborhood" placeholder="Rogers Park" />
                  <span class="field__hint">We show artists living within six miles of the gallery.</span>
                </label>
                <label class="field">
                  <span class="field__label">ZIP</span>
                  <input name="zip" inputmode="numeric" maxlength="5" [(ngModel)]="zip" autocomplete="postal-code" placeholder="60660" />
                </label>
                <label class="field">
                  <span class="field__label">Website or portfolio</span>
                  <input name="website" type="url" [(ngModel)]="website" placeholder="https://" />
                </label>
                <label class="field">
                  <span class="field__label">Instagram</span>
                  <input name="instagram" [(ngModel)]="instagram" placeholder="@yourhandle" />
                </label>
              </div>
            </fieldset>

            <!-- 2 ---------------------------------------------------------------- -->
            <fieldset class="sform__step" reveal>
              <legend class="sform__legend"><span class="sform__num">2</span> Your work</legend>
              <div class="sform__grid">
                <label class="field select">
                  <span class="field__label">Primary medium</span>
                  <select name="medium" [(ngModel)]="medium">
                    <option value="">Choose one…</option>
                    @for (m of mediums; track m) { <option [value]="m">{{ m }}</option> }
                  </select>
                </label>
                <label class="field">
                  <span class="field__label">Anything else you work in</span>
                  <input name="otherMedia" [(ngModel)]="otherMedia" placeholder="Monotype, encaustic…" />
                </label>
                <label class="field">
                  <span class="field__label">Years making this work</span>
                  <input name="years" type="number" min="0" max="80" [(ngModel)]="years" placeholder="8" />
                </label>
                <label class="field select">
                  <span class="field__label">Typical price range</span>
                  <select name="priceBand" [(ngModel)]="priceBand">
                    <option value="">Choose one…</option>
                    @for (p of priceBands; track p) { <option [value]="p">{{ p }}</option> }
                  </select>
                </label>
              </div>
              <label class="field field--wide">
                <span class="field__label">Short bio</span>
                <textarea name="bio" rows="3" maxlength="300" [(ngModel)]="bio"
                  placeholder="Two or three sentences we can put next to your work."></textarea>
                <span class="field__hint sform__count">{{ 300 - bio().length }} characters left</span>
              </label>
              <label class="field field--wide">
                <span class="field__label">Artist statement</span>
                <textarea name="statement" rows="6" maxlength="1200" [(ngModel)]="statement"
                  placeholder="What you make, how you make it and what you are after."></textarea>
                <span class="field__hint sform__count">{{ 1200 - statement().length }} characters left</span>
              </label>
            </fieldset>

            <!-- 3 ---------------------------------------------------------------- -->
            <fieldset class="sform__step" reveal>
              <legend class="sform__legend"><span class="sform__num">3</span> What you are here for</legend>
              <p class="field__hint">Pick as many as apply. Each one opens a couple of extra questions.</p>
              <div class="filters" role="group" aria-label="What you are here for">
                @for (g of goals; track g.id) {
                  <button type="button" class="chip" [class.is-on]="has(g.id)" [attr.aria-pressed]="has(g.id)"
                    (click)="toggle(g.id)">{{ g.label }}</button>
                }
              </div>

              @if (has('sell')) {
                <div class="sform__panel">
                  <h3 class="sform__panel-title"><app-icon name="cart" [size]="20" /> Selling in the shop</h3>
                  <div class="sform__grid">
                    <label class="field">
                      <span class="field__label">Pieces ready to go</span>
                      <input name="piecesReady" type="number" min="0" [(ngModel)]="piecesReady" placeholder="12" />
                    </label>
                    <label class="field select">
                      <span class="field__label">Can you restock?</span>
                      <select name="restock" [(ngModel)]="restock">
                        <option value="">Choose one…</option>
                        <option value="yes">Yes, regularly</option>
                        <option value="limited">A few at a time</option>
                        <option value="no">One-of-a-kind only</option>
                      </select>
                    </label>
                  </div>
                </div>
              }

              @if (has('exhibit')) {
                <div class="sform__panel">
                  <h3 class="sform__panel-title"><app-icon name="frame" [size]="20" /> Showing in the gallery</h3>
                  <div class="sform__grid">
                    <label class="field">
                      <span class="field__label">Proposed show title</span>
                      <input name="showTitle" [(ngModel)]="showTitle" placeholder="Working title is fine" />
                    </label>
                    <label class="field">
                      <span class="field__label">Number of works</span>
                      <input name="workCount" type="number" min="1" [(ngModel)]="workCount" placeholder="18" />
                    </label>
                    <label class="field">
                      <span class="field__label">Largest piece (inches)</span>
                      <input name="largest" [(ngModel)]="largest" placeholder="48 × 36" />
                    </label>
                    <label class="field select">
                      <span class="field__label">When would suit you</span>
                      <select name="showWindow" [(ngModel)]="showWindow">
                        <option value="">Choose one…</option>
                        @for (w of windows; track w) { <option [value]="w">{{ w }}</option> }
                      </select>
                    </label>
                  </div>
                  <fieldset class="picks">
                    <legend class="field__label">Gallery</legend>
                    @for (g of ['East gallery', 'West gallery', 'Either']; track g) {
                      <label class="pick">
                        <input type="radio" name="gallery" [value]="g" [(ngModel)]="gallery" />
                        <span>{{ g }}</span>
                      </label>
                    }
                  </fieldset>
                </div>
              }

              @if (has('teach')) {
                <div class="sform__panel">
                  <h3 class="sform__panel-title"><app-icon name="brush" [size]="20" /> Teaching a class</h3>
                  <div class="sform__grid">
                    <label class="field">
                      <span class="field__label">Class title</span>
                      <input name="classTitle" [(ngModel)]="classTitle" placeholder="Intro to drypoint" />
                    </label>
                    <label class="field">
                      <span class="field__label">Max students</span>
                      <input name="maxStudents" type="number" min="1" max="24" [(ngModel)]="maxStudents" placeholder="10" />
                    </label>
                  </div>
                  <fieldset class="picks">
                    <legend class="field__label">Format</legend>
                    @for (f of formats; track f) {
                      <label class="pick">
                        <input type="radio" name="format" [value]="f" [(ngModel)]="format" />
                        <span>{{ f }}</span>
                      </label>
                    }
                  </fieldset>
                  <label class="field field--wide">
                    <span class="field__label">Materials students need</span>
                    <textarea name="materials" rows="3" [(ngModel)]="materials"
                      placeholder="What we supply, what they bring."></textarea>
                  </label>
                  <label class="check">
                    <input type="checkbox" name="taughtBefore" [(ngModel)]="taughtBefore" />
                    <span>I have taught a group like this before</span>
                  </label>
                </div>
              }
            </fieldset>

            <!-- 4 ---------------------------------------------------------------- -->
            <fieldset class="sform__step" reveal>
              <legend class="sform__legend"><span class="sform__num">4</span> Images</legend>
              <p class="field__hint">Three to five pieces, JPG or PNG, as large as you have. Nothing uploads from this stub.</p>
              <label class="field field--wide">
                <span class="field__label">Images of your work</span>
                <input class="file" name="images" type="file" accept="image/*" multiple (change)="pickFiles($event)" />
              </label>
              @if (files().length) {
                <ul class="sform__files">
                  @for (f of files(); track f) { <li><app-icon name="frame" [size]="16" /> {{ f }}</li> }
                </ul>
              }
              <label class="field field--wide">
                <span class="field__label">Or a link to a folder / portfolio</span>
                <input name="portfolio" type="url" [(ngModel)]="portfolio" placeholder="https://" />
              </label>
            </fieldset>

            <!-- 5 ---------------------------------------------------------------- -->
            <fieldset class="sform__step" reveal>
              <legend class="sform__legend"><span class="sform__num">5</span> Before you send</legend>
              <label class="field select field--wide">
                <span class="field__label">How did you hear about us</span>
                <select name="heard" [(ngModel)]="heard">
                  <option value="">Choose one…</option>
                  @for (h of heardOptions; track h) { <option [value]="h">{{ h }}</option> }
                </select>
              </label>
              <label class="check">
                <input type="checkbox" name="ownWork" [(ngModel)]="ownWork" />
                <span>The work is mine and I hold the rights to it <i>required</i></span>
              </label>
              <label class="check">
                <input type="checkbox" name="newsletter" [(ngModel)]="newsletter" />
                <span>Email me openings, new classes and artist calls</span>
              </label>
              <!-- Spam trap: people never see or fill this. -->
              <input class="hp" name="company" tabindex="-1" autocomplete="off" [(ngModel)]="trap" aria-hidden="true" />

              @if (error()) { <p class="subscribe__err" role="alert">{{ error() }}</p> }

              <div class="btn-row" style="justify-content: flex-start">
                <button class="btn btn-dark btn-lg" type="submit">Send my submission</button>
                <a class="btn btn-border-dark btn-lg" routerLink="/submit">What we are looking for</a>
              </div>
            </fieldset>
          </form>
        }
      </div>
    </section>
  `,
})
export class ArtistSubmission {
  protected site = SITE;
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
  protected files = signal<string[]>([]);
  protected portfolio = signal('');
  // 5 · the rest
  protected heard = signal('');
  protected ownWork = signal(false);
  protected newsletter = signal(true);
  protected trap = signal('');

  protected sent = signal(false);
  protected error = signal('');

  protected has(g: Goal): boolean {
    return this.wants().includes(g);
  }

  protected toggle(g: Goal): void {
    this.wants.update((w) => (w.includes(g) ? w.filter((x) => x !== g) : [...w, g]));
  }

  protected pickFiles(e: Event): void {
    const picked = (e.target as HTMLInputElement).files;
    this.files.set(picked ? [...picked].map((f) => `${f.name} (${Math.round(f.size / 1024)} KB)`) : []);
  }

  /** What a real POST body would look like. Shown back to the applicant while this page is a stub. */
  protected payload = computed(() =>
    JSON.stringify(
      {
        artist: {
          name: this.name(), pronouns: this.pronouns(), email: this.email(), phone: this.phone(),
          neighborhood: this.neighborhood(), zip: this.zip(), website: this.website(), instagram: this.instagram(),
        },
        work: {
          medium: this.medium(), otherMedia: this.otherMedia(), years: this.years(),
          priceBand: this.priceBand(), bio: this.bio(), statement: this.statement(),
        },
        wants: this.wants(),
        sell: this.has('sell') ? { piecesReady: this.piecesReady(), restock: this.restock() } : undefined,
        exhibit: this.has('exhibit')
          ? { showTitle: this.showTitle(), workCount: this.workCount(), largest: this.largest(), window: this.showWindow(), gallery: this.gallery() }
          : undefined,
        teach: this.has('teach')
          ? { classTitle: this.classTitle(), maxStudents: this.maxStudents(), format: this.format(), materials: this.materials(), taughtBefore: this.taughtBefore() }
          : undefined,
        images: this.files(),
        portfolio: this.portfolio(),
        heard: this.heard(),
        consent: { ownWork: this.ownWork(), newsletter: this.newsletter() },
      },
      null,
      2,
    ),
  );

  protected send(): void {
    if (!this.name().trim()) return this.error.set('Please tell us your name.');
    if (!/^\S+@\S+\.\S+$/.test(this.email())) return this.error.set('That email address does not look right.');
    if (!this.wants().length) return this.error.set('Pick at least one thing you are here for.');
    if (!this.ownWork()) return this.error.set('Please confirm the work is yours.');
    // Stub: a real build posts payload() to the API here.
    this.error.set('');
    this.sent.set(true);
  }

  constructor() {
    inject(Seo).set({
      title: 'Artist Submission Form | Teach, Exhibit or Sell at 6311arts',
      description:
        'Submit your work to 6311arts: one form for selling in the shop, showing in the galleries and teaching a class on Chicago’s North Side.',
      path: '/artists/submit',
      noindex: true,
      breadcrumbs: [{ name: 'Artists', path: '/artists' }, { name: 'Submit your work', path: '/artists/submit' }],
    });
  }
}
