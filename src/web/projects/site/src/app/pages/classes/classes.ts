import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { combineLatest } from 'rxjs';
import { ArtClass, ClassType, Exhibition } from '../../core/models';
import { Seo, eventLd } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { IN_PAGE } from '../../core/navigation';
import { SiteApi } from '../../services/site-api.service';
import { slugify } from '../../data/mock-shop';
import { STUDIO_PHOTOS } from '../../data/venue-photos';
import { ClassCard } from '../../shared/cards/class-card/class-card';
import { ShowCard, ShowMoment } from '../../shared/cards/show-card/show-card';
import { WhenPipe, addDays, chicagoDay, chicagoTime, endOfWeek } from '../../shared/format';
import { Hero } from '../../shared/hero/hero';
import { Newsletter } from '../../shared/forms/newsletter/newsletter';
import { Reveal } from '../../shared/reveal';

/**
 * The calendar: every upcoming class, filterable by medium and level, shown as cards grouped by month (grid)
 * or as a month calendar. Both views also carry the gallery's exhibitions, styled apart from classes: each show's
 * opening, reception and last day sit on their dates, and the month calendar lists the shows on view that month
 * above its grid. Filters, view and month live in the URL.
 */
@Component({
  selector: 'app-classes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ClassCard, ShowCard, Newsletter, RouterLink, UiIcon, Reveal, WhenPipe],
  templateUrl: './classes.html',
})
export class Classes {
  private api = inject(SiteApi);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  /**
   * Classes and exhibitions land together, so each list renders once in its final order. When the browser has to
   * refetch after server rendering, a list built from one feed and then patched with the other hydrates out of
   * order (the exhibition card ends up above earlier classes).
   */
  private feeds = toSignal(combineLatest([this.api.classes(), this.api.exhibitions()]), {
    initialValue: [[], []] as [ArtClass[], Exhibition[]],
  });
  protected all = computed(() => this.feeds()[0]);
  private exhibitions = computed(() => this.feeds()[1]);
  private query = toSignal(this.route.queryParamMap, { requireSync: true });
  protected type = computed(() => this.query().get('type') ?? 'All');
  protected level = computed(() => this.query().get('level') ?? '');
  protected view = computed(() => (this.query().get('view') === 'calendar' ? 'calendar' : 'grid'));
  protected levels = ['Beginner', 'Intermediate'];

  protected types = computed(
    () => ['All', ...new Set(this.all().map((c) => c.type))] as (ClassType | 'All')[],
  );
  /** Flyers are portrait posters, so the banner shows the studio and the cards carry the flyers. */
  protected studioPhotos = STUDIO_PHOTOS;

  /**
   * What's on today (not yet over) and later this week (through Sunday), in Chicago time and ignoring the filters.
   * Uses every session of every class, so a course still meeting after its first week stays listed (its card
   * reads "Week n of m"), and every exhibition's opening, reception and last day.
   */
  protected soon = computed(() => {
    const now = Date.now();
    const day = chicagoDay(now);
    const clock = chicagoTime(now);
    const sunday = endOfWeek(day);
    const today: Entry[] = [];
    const week: Entry[] = [];
    const add = (x: Entry, over: boolean) => {
      if (x.date === day) {
        if (!over) today.push(x);
      } else if (x.date > day && x.date <= sunday) {
        week.push(x);
      }
    };
    // A session meets at its first session's wall-clock time, so it is over once Chicago passes that end time.
    for (const s of sessionsOf(this.all()))
      add(
        { key: `c${s.c.id}-${s.n}`, date: s.date, time: s.c.start.slice(11), s },
        s.c.end.slice(11, 16) <= clock,
      );
    // A show's opening or last day lasts all day; only a reception is over once it ends.
    for (const m of milestonesOf(this.exhibitions()))
      add(
        { key: `e${m.e.slug}-${m.kind}`, date: m.date, time: momentTime(m), m },
        m.kind === 'reception' && !!m.e.reception && Date.parse(m.e.reception.end) <= now,
      );
    const order = (a: Entry, b: Entry) =>
      a.date.localeCompare(b.date) || a.time.localeCompare(b.time);
    return { today: today.sort(order), week: week.sort(order) };
  });

  protected filtered = computed(() =>
    this.all().filter(
      (c) =>
        (this.type() === 'All' || c.type === this.type()) &&
        (!this.level() || c.level === this.level() || c.level === 'All levels'),
    ),
  );

  /**
   * Grid view: one card per upcoming session (each week of a course gets its own card) and per upcoming exhibition
   * date, grouped by month. Within a day, all-day show dates come first, then the rest by start time.
   */
  protected grouped = computed(() => {
    const entries: Entry[] = [
      ...this.sessions().map((s) => ({
        key: `c${s.c.id}-${s.n}`,
        date: s.date,
        time: s.c.start.slice(11),
        s,
      })),
      ...this.milestones().map((m) => ({
        key: `e${m.e.slug}-${m.kind}`,
        date: m.date,
        time: momentTime(m),
        m,
      })),
    ].sort((a, b) => a.date.localeCompare(b.date) || a.time.localeCompare(b.time));
    const by = new Map<string, Entry[]>();
    for (const x of entries) {
      if (x.date < this.today) continue;
      const key = monthLabel(x.date.slice(0, 7));
      by.set(key, [...(by.get(key) ?? []), x]);
    }
    return [...by].map(([month, items]) => ({ month, items }));
  });

  /** Whether any exhibition date is still ahead, so a class filter that matches nothing still leaves something to show. */
  private upcomingShows = computed(() => this.milestones().some((m) => m.date >= this.today));

  /** Calendar view: a Monday–Sunday month grid with every session of every class on its Chicago date. */
  protected readonly today = chicagoDay();
  protected readonly thisMonth = this.today.slice(0, 7);
  protected weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  protected monthLabel = monthLabel;
  /** Classes carry an instructor's name, not an id; the profile page is keyed on the slugified name. */
  protected slug = slugify;

  /** Each session of the filtered classes, in date order. */
  private sessions = computed(() => sessionsOf(this.filtered()));

  /**
   * Exhibitions on the calendar. A show has no medium, so picking one hides them; it is open to everyone, so a
   * level filter keeps them, the way it keeps an "All levels" class.
   */
  protected shows = computed(() => (this.type() === 'All' ? this.exhibitions() : []));

  /** Each show's dated moments, in date order. A reception on opening day stands in for "opens". */
  private milestones = computed(() => milestonesOf(this.shows()));

  /** Every dated thing on the calendar, class sessions and show moments together, in date order. */
  private dates = computed(() =>
    [...this.sessions().map((s) => s.date), ...this.milestones().map((m) => m.date)].sort(),
  );

  /** The month in the URL, else the month of the next date that matches the filters, else this month. */
  protected month = computed(() => {
    const m = this.query().get('month');
    if (m && /^\d{4}-(0[1-9]|1[0-2])$/.test(m)) return m;
    const next = this.dates().find((d) => d >= this.today);
    return next ? next.slice(0, 7) : this.thisMonth;
  });

  protected lastMonth = computed(() => {
    const m = this.dates().at(-1)?.slice(0, 7) ?? this.thisMonth;
    return m > this.thisMonth ? m : this.thisMonth;
  });

  protected calendar = computed(() => {
    const month = this.month();
    const byDay = new Map<string, Session[]>();
    for (const s of this.sessions()) byDay.set(s.date, [...(byDay.get(s.date) ?? []), s]);
    const showsByDay = new Map<string, Milestone[]>();
    for (const m of this.milestones())
      showsByDay.set(m.date, [...(showsByDay.get(m.date) ?? []), m]);
    const [y, m] = month.split('-').map(Number);
    const first = new Date(Date.UTC(y, m - 1, 1));
    const cursor = new Date(first);
    cursor.setUTCDate(1 - ((first.getUTCDay() + 6) % 7));
    const end = endOfWeek(new Date(Date.UTC(y, m, 0)).toISOString().slice(0, 10));
    const days: {
      date: string;
      num: number;
      inMonth: boolean;
      today: boolean;
      items: Session[];
      shows: Milestone[];
    }[] = [];
    for (;;) {
      const date = cursor.toISOString().slice(0, 10);
      days.push({
        date,
        num: cursor.getUTCDate(),
        inMonth: date.startsWith(month),
        today: date === this.today,
        items: byDay.get(date) ?? [],
        shows: showsByDay.get(date) ?? [],
      });
      if (date === end) break;
      cursor.setUTCDate(cursor.getUTCDate() + 1);
    }
    const count = days.reduce((n, d) => n + (d.inMonth ? d.items.length + d.shows.length : 0), 0);
    const later = this.dates().find((d) => d.slice(0, 7) > month);
    const onView = this.shows()
      .filter((e) => dayOf(e.start).slice(0, 7) <= month && dayOf(e.end).slice(0, 7) >= month)
      .sort((a, b) => a.start.localeCompare(b.start));
    return { label: monthLabel(month), days, count, onView, next: later?.slice(0, 7) ?? null };
  });

  constructor() {
    const seo = inject(Seo);
    effect(() =>
      seo.set({
        title: 'Art Classes & Workshops in Chicago: Screen Printing, Drawing & More',
        description:
          'Small-group art classes in Edgewater, Chicago, taught by local artists: screen printing, printmaking, scratchboard, talks and free workshops. All materials included.',
        path: '/classes',
        breadcrumbs: [{ name: 'Classes', path: '/classes' }],
        jsonLd: this.all().map((c) => eventLd(c, `${SITE.url}/classes/${c.slug}`)),
      }),
    );
  }

  setType(t: string) {
    this.nav({ type: t === 'All' ? null : t });
  }
  setLevel(l: string) {
    this.nav({ level: this.level() === l ? null : l });
  }
  reset() {
    this.nav({ type: null, level: null });
  }
  setView(v: 'grid' | 'calendar') {
    this.nav({ view: v === 'grid' ? null : v });
  }
  step(n: number) {
    this.goMonth(shiftMonth(this.month(), n));
  }
  goMonth(month: string) {
    this.nav({ month });
  }
  private nav(queryParams: Record<string, string | null>) {
    // Filters and the grid/calendar switch update the list in place: no scroll to top, no page cross-fade.
    this.router.navigate([], {
      queryParams,
      queryParamsHandling: 'merge',
      replaceUrl: true,
      scroll: 'manual',
      info: IN_PAGE,
    });
  }
}

/** "2026-11" moved by n months. */
function shiftMonth(month: string, n: number): string {
  const [y, m] = month.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1 + n, 1)).toISOString().slice(0, 7);
}

/** "2026-11" as "November 2026". */
function monthLabel(month: string): string {
  const [y, m] = month.split('-').map(Number);
  return new Intl.DateTimeFormat('en-US', {
    timeZone: 'UTC',
    month: 'long',
    year: 'numeric',
  }).format(new Date(Date.UTC(y, m - 1, 1)));
}

/**
 * Each session of the given classes, in date order. Multi-session classes are weekly courses ("5 weeks"), so
 * session n falls n weeks after the first, at the same wall-clock time.
 */
function sessionsOf(classes: ArtClass[]): Session[] {
  return classes
    .flatMap((c) => {
      const first = chicagoDay(c.start);
      return Array.from({ length: Math.max(1, c.sessions) }, (_, i) => ({
        c,
        n: i + 1,
        date: addDays(first, i * 7),
      }));
    })
    .sort(
      (a, b) =>
        a.date.localeCompare(b.date) || a.c.start.slice(11).localeCompare(b.c.start.slice(11)),
    );
}

/** An exhibition's date: a bare YYYY-MM-DD as given, else the Chicago day of the instant. */
function dayOf(iso: string): string {
  return iso.length === 10 ? iso : chicagoDay(iso);
}

/** Each show's dated moments, in date order. A reception on opening day stands in for "opens". */
function milestonesOf(exhibitions: Exhibition[]): Milestone[] {
  return exhibitions
    .flatMap((e) => {
      const start = dayOf(e.start);
      const end = dayOf(e.end);
      const reception = e.reception ? chicagoDay(e.reception.start) : null;
      const out: Milestone[] = [];
      if (reception !== start) out.push({ e, kind: 'opens', date: start });
      if (reception) out.push({ e, kind: 'reception', date: reception });
      if (end !== start && end !== reception) out.push({ e, kind: 'closes', date: end });
      return out;
    })
    .sort((a, b) => a.date.localeCompare(b.date));
}

/** A moment's wall-clock time for ordering within a day: a reception's start; all-day moments sort first. */
function momentTime(m: Milestone): string {
  return m.kind === 'reception' ? (m.e.reception?.start.slice(11) ?? '') : '';
}

/** One meeting of a class: the class, which week it is (1-based), and its Chicago date (YYYY-MM-DD). */
interface Session {
  c: ArtClass;
  n: number;
  date: string;
}

/** A dated moment in an exhibition's run, on its Chicago date (YYYY-MM-DD). */
interface Milestone {
  e: Exhibition;
  kind: ShowMoment;
  date: string;
}

/**
 * A card under Today, This week or in the grid view: a class session or an exhibition date, keyed for tracking and
 * sortable by date and time.
 */
interface Entry {
  key: string;
  date: string;
  time: string;
  s?: Session;
  m?: Milestone;
}
