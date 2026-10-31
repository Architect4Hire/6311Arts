import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Seo, eventLd } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { LegacyStore } from '../../core/legacy-store';
import { slugify } from '../../data/mock-shop';
import { STUDIO_PHOTOS } from '../../data/venue-photos';
import { ArtImage } from '../../shared/media/art-image/art-image';
import { ClassCard } from '../../shared/cards/class-card/class-card';
import { MoneyPipe, WhenPipe } from '../../shared/format';
import { Hero } from '../../shared/hero/hero';
import { NotFound } from '../not-found/not-found';

/**
 * One class: the copy staff wrote for it, and every run still to come (ADR 0065).
 *
 * **The page is the class, not its next run.** It reads `/site/classes/{slug}`, which answers for a published
 * class whether or not anything is on the calendar — so a class between terms keeps its address instead of
 * answering 404 while the sitemap still names it, and a draft being written can be previewed before it has dates.
 * The soonest run leads the page, as it always has; any others are listed under it.
 */
@Component({
  selector: 'app-class-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ArtImage, ClassCard, UiIcon, MoneyPipe, WhenPipe, RouterLink, NotFound],
  templateUrl: './class-detail.html',
})
export class ClassDetail {
  /** The route's `:slug`, bound by the router (`withComponentInputBinding`), or the preview page's (ADR 0063). */
  readonly slug = input.required<string>();

  private api = inject(SiteApi);
  protected store = inject(LegacyStore);
  protected site = SITE;
  /** Instructor profiles are keyed on the slugified name, since classes carry only the name. */
  protected instructorSlug = slugify;
  /** The flyer is shown whole further down the page, so the banner shows the room the class runs in. */
  protected studioPhotos = STUDIO_PHOTOS;

  private tiers = toSignal(this.api.memberships(), { initialValue: [] });

  private data = toSignal(
    toObservable(this.slug).pipe(
      switchMap((slug) => this.api.classPage(slug).pipe(map((page) => ({ page })))),
    ),
  );
  protected loaded = computed(() => this.data() !== undefined);
  protected klass = computed(() => this.data()?.page ?? undefined);
  /** The read failed rather than came back empty: the server is answering 503, so this is not a 404 (ADR 0065). */
  protected unavailable = computed(() => this.data()?.page === null);

  /** The run that leads the page: the soonest still to come, and nothing at all between terms. */
  protected cls = computed(() => this.klass()?.runs[0]);
  /** The class's other dates, which the page used to hide. */
  protected otherRuns = computed(() => this.klass()?.runs.slice(1) ?? []);

  private all = toSignal(toObservable(this.slug).pipe(switchMap(() => this.api.classes())));
  protected more = computed(() =>
    (this.all() ?? []).filter((c) => c.slug !== this.slug()).slice(0, 3),
  );

  /** The most a member pays: the price less the smallest tier discount. */
  protected memberPrice = computed(() => {
    const discounts = this.tiers().map((t) => t.discount);
    return discounts.length
      ? Math.round(((this.cls()?.price ?? 0) * (100 - Math.min(...discounts))) / 100)
      : undefined;
  });
  /** The cheapest tier Square has priced. */
  protected joinFrom = computed(() => {
    const prices = this.tiers().flatMap((t) => (t.price == null ? [] : [t.price]));
    return prices.length ? Math.min(...prices) : undefined;
  });

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const klass = this.klass();
      if (!klass) return;
      const run = this.cls();
      const path = `/classes/${klass.slug}`;
      seo.set({
        title: `${klass.title} | Art Class in Chicago`,
        description: `${klass.summary}${run && run.sessions > 1 ? ` ${run.sessions}-week course.` : ''}${run?.instructor ? ` With ${run.instructor}.` : ''} At 6311arts, Edgewater.`,
        path,
        image: klass.image,
        type: 'event',
        breadcrumbs: [
          { name: 'Classes', path: '/classes' },
          { name: klass.title, path },
        ],
        // An event needs a date: a class with nothing on the calendar is described by the page, not as an event.
        jsonLd: run ? [eventLd(run, SITE.url + path)] : [],
      });
    });
  }
}
