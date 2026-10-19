import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ArtClass, ClassType } from '../core/models';
import { Seo, eventLd } from '../core/seo.service';
import { SITE } from '../core/site';
import { IN_PAGE } from '../core/navigation';
import { WpApi } from '../core/wp-api.service';
import { STUDIO_PHOTOS } from '../data/venue-photos';
import { ClassCard } from '../shared/cards';
import { WhenPipe, addDays, chicagoDay, endOfWeek } from '../shared/format';
import { Hero } from '../shared/hero';
import { Icon } from '../shared/icon';
import { Newsletter } from '../shared/newsletter';
import { Reveal } from '../shared/reveal';

/**
 * The calendar: every upcoming class, filterable by medium and level, shown as cards grouped by month (grid)
 * or as a month calendar. Filters, view and month live in the URL.
 */
@Component({
  selector: 'app-classes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ClassCard, Newsletter, RouterLink, Icon, Reveal, WhenPipe],
  template: `
    <app-hero title="Classes, workshops & events" eyebrow="Taught by local artists"
      lead="Small groups, hands-on, and open to first-timers. Pick a night and make something."
      [images]="studioPhotos" />

    <section class="band band--white" id="content">
      <div class="container">
        @let s = soon();
        @if (s.today.length || s.week.length) {
          <div class="soon">
            <h2 class="month" reveal>Today</h2>
            @if (s.today.length) {
              <div class="class-list class-list--full">
                @for (c of s.today; track c.id) {
                  <app-class-card [item]="c" reveal />
                }
              </div>
            } @else {
              <p class="soon__none">Nothing left on the calendar today.</p>
            }
            @if (s.week.length) {
              <h2 class="month" reveal>This week</h2>
              <div class="class-list class-list--full">
                @for (c of s.week; track c.id) {
                  <app-class-card [item]="c" reveal />
                }
              </div>
            }
          </div>
        }

        <div class="all-head" reveal>
          <h2 class="month month--all">All events</h2>
          <div class="view-switch" role="group" aria-label="Choose view">
            <button type="button" class="chip chip--icon" [class.is-on]="view() === 'grid'" (click)="setView('grid')" [attr.aria-pressed]="view() === 'grid'">
              <app-icon name="grid" [size]="16" [stroke]="2.4" /> Grid
            </button>
            <button type="button" class="chip chip--icon" [class.is-on]="view() === 'calendar'" (click)="setView('calendar')" [attr.aria-pressed]="view() === 'calendar'">
              <app-icon name="calendar" [size]="16" [stroke]="2.4" /> Calendar
            </button>
          </div>
        </div>
        <div class="filters" role="toolbar" aria-label="Filter classes">
          @for (t of types(); track t) {
            <button type="button" class="chip" [class.is-on]="type() === t" (click)="setType(t)" [attr.aria-pressed]="type() === t">{{ t }}</button>
          }
          <span class="filters__sep"></span>
          @for (l of levels; track l) {
            <button type="button" class="chip chip--ghost" [class.is-on]="level() === l" (click)="setLevel(l)" [attr.aria-pressed]="level() === l">{{ l }}</button>
          }
        </div>

        @if (!filtered().length) {
          <p class="empty">Nothing on the calendar matches that yet. <button class="linklike" (click)="reset()">Show everything</button> or get notified below when new classes drop.</p>
        } @else if (view() === 'calendar') {
          @let cal = calendar();
          <div class="cal">
            <div class="cal__head">
              <button type="button" class="cal__step cal__step--prev" (click)="step(-1)" [disabled]="month() <= thisMonth" aria-label="Previous month">
                <app-icon name="arrow" [size]="20" [stroke]="2.4" />
              </button>
              <h3 class="cal__title" aria-live="polite">{{ cal.label }}</h3>
              <button type="button" class="cal__step" (click)="step(1)" [disabled]="month() >= lastMonth()" aria-label="Next month">
                <app-icon name="arrow" [size]="20" [stroke]="2.4" />
              </button>
            </div>
            <ol class="cal__weekdays" aria-hidden="true">
              @for (w of weekdays; track w) {
                <li>{{ w }}</li>
              }
            </ol>
            <ol class="cal__grid">
              @for (d of cal.days; track d.date) {
                <li class="cal__day" [class.is-out]="!d.inMonth" [class.is-today]="d.today" [class.has-events]="d.items.length">
                  <span class="cal__label" aria-hidden="true">
                    <span class="cal__num">{{ d.num }}</span>
                    <span class="cal__wd">{{ d.date | when: 'dow' }}</span>
                  </span>
                  <span class="sr-only">{{ d.date | when }}</span>
                  @if (d.items.length) {
                    <span class="cal__events">
                      @for (s of d.items; track s.c.id) {
                        <a class="cal__event" [routerLink]="['/classes', s.c.slug]">
                          <span class="cal__time">
                            {{ s.c.start | when: 'time' }}
                            @if (s.c.sessions > 1) { · Week {{ s.n }} of {{ s.c.sessions }} }
                          </span>
                          {{ s.c.title }}
                        </a>
                      }
                    </span>
                  }
                </li>
              }
            </ol>
            @if (!cal.count) {
              <p class="cal__none">
                No classes in {{ cal.label }}.
                @if (cal.next) {
                  <button class="linklike" (click)="goMonth(cal.next)">Jump to {{ monthLabel(cal.next) }}</button>
                }
              </p>
            }
          </div>
        } @else {
          @for (g of grouped(); track g.month) {
            <h2 class="month" reveal>{{ g.month }}</h2>
            <div class="class-list class-list--full">
              @for (s of g.items; track s.c.id + '-' + s.n) {
                <app-class-card [item]="s.c" [date]="s.date" [week]="s.c.sessions > 1 ? s.n : undefined" reveal />
              }
            </div>
          }
        }
      </div>
    </section>

    <section class="band band--light text-center">
      <div class="container container--mid" reveal>
        <app-icon name="briefcase" [size]="64" [stroke]="1" />
        <h2 class="band__title">Free for working artists</h2>
        <p class="band__lead">Professional development sessions on pricing, portfolios and grants, plus free art therapy, are always free. Want to teach here? We’re always looking for North Side artists to lead classes.</p>
        <a class="btn btn-dark btn-lg" routerLink="/submit">Pitch a class</a>
      </div>
    </section>

    <app-newsletter heading="New classes drop fast" lead="Screen printing sells out. Get the calendar in your inbox the day it’s posted." />
  `,
})
export class Classes {
  private api = inject(WpApi);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  protected all = toSignal(this.api.classes(), { initialValue: [] });
  private query = toSignal(this.route.queryParamMap, { requireSync: true });
  protected type = computed(() => this.query().get('type') ?? 'All');
  protected level = computed(() => this.query().get('level') ?? '');
  protected view = computed(() => (this.query().get('view') === 'calendar' ? 'calendar' : 'grid'));
  protected levels = ['Beginner', 'Intermediate'];

  protected types = computed(() => ['All', ...new Set(this.all().map((c) => c.type))] as (ClassType | 'All')[]);
  /** Flyers are portrait posters, so the banner shows the studio and the cards carry the flyers. */
  protected studioPhotos = STUDIO_PHOTOS;

  /**
   * What's on today (not yet over) and later this week (through Sunday), in Chicago time and ignoring the filters.
   * Uses each class's first session, the same date its card shows.
   */
  protected soon = computed(() => {
    const now = Date.now();
    const day = chicagoDay(now);
    const sunday = endOfWeek(day);
    const today: ArtClass[] = [];
    const week: ArtClass[] = [];
    for (const c of this.all()) {
      const d = chicagoDay(c.start);
      if (d === day) {
        if (Date.parse(c.end) > now) today.push(c);
      } else if (d > day && d <= sunday) {
        week.push(c);
      }
    }
    return { today, week };
  });

  protected filtered = computed(() =>
    this.all().filter(
      (c) => (this.type() === 'All' || c.type === this.type()) && (!this.level() || c.level === this.level() || c.level === 'All levels'),
    ),
  );

  /** Grid view: one card per upcoming session (each week of a course gets its own card), grouped by month. */
  protected grouped = computed(() => {
    const by = new Map<string, Session[]>();
    for (const s of this.sessions()) {
      if (s.date < this.today) continue;
      const key = monthLabel(s.date.slice(0, 7));
      by.set(key, [...(by.get(key) ?? []), s]);
    }
    return [...by].map(([month, items]) => ({ month, items }));
  });

  /** Calendar view: a Monday–Sunday month grid with every session of every class on its Chicago date. */
  protected readonly today = chicagoDay();
  protected readonly thisMonth = this.today.slice(0, 7);
  protected weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  protected monthLabel = monthLabel;

  /**
   * Each session of the filtered classes, in date order. Multi-session classes are weekly courses
   * ("5 weeks"), so session n falls n weeks after the first, at the same wall-clock time.
   */
  private sessions = computed<Session[]>(() =>
    this.filtered()
      .flatMap((c) => {
        const first = chicagoDay(c.start);
        return Array.from({ length: Math.max(1, c.sessions) }, (_, i) => ({ c, n: i + 1, date: addDays(first, i * 7) }));
      })
      .sort((a, b) => a.date.localeCompare(b.date) || a.c.start.slice(11).localeCompare(b.c.start.slice(11))),
  );

  /** The month in the URL, else the month of the next session that matches the filters, else this month. */
  protected month = computed(() => {
    const m = this.query().get('month');
    if (m && /^\d{4}-(0[1-9]|1[0-2])$/.test(m)) return m;
    const next = this.sessions().find((s) => s.date >= this.today);
    return next ? next.date.slice(0, 7) : this.thisMonth;
  });

  protected lastMonth = computed(() => {
    const m = this.sessions().at(-1)?.date.slice(0, 7) ?? this.thisMonth;
    return m > this.thisMonth ? m : this.thisMonth;
  });

  protected calendar = computed(() => {
    const month = this.month();
    const byDay = new Map<string, Session[]>();
    for (const s of this.sessions()) byDay.set(s.date, [...(byDay.get(s.date) ?? []), s]);
    const [y, m] = month.split('-').map(Number);
    const first = new Date(Date.UTC(y, m - 1, 1));
    const cursor = new Date(first);
    cursor.setUTCDate(1 - ((first.getUTCDay() + 6) % 7));
    const end = endOfWeek(new Date(Date.UTC(y, m, 0)).toISOString().slice(0, 10));
    const days: { date: string; num: number; inMonth: boolean; today: boolean; items: Session[] }[] = [];
    for (;;) {
      const date = cursor.toISOString().slice(0, 10);
      days.push({ date, num: cursor.getUTCDate(), inMonth: date.startsWith(month), today: date === this.today, items: byDay.get(date) ?? [] });
      if (date === end) break;
      cursor.setUTCDate(cursor.getUTCDate() + 1);
    }
    const count = days.reduce((n, d) => n + (d.inMonth ? d.items.length : 0), 0);
    const later = this.sessions().find((s) => s.date.slice(0, 7) > month);
    return { label: monthLabel(month), days, count, next: later?.date.slice(0, 7) ?? null };
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
    this.router.navigate([], { queryParams, queryParamsHandling: 'merge', replaceUrl: true, scroll: 'manual', info: IN_PAGE });
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
  return new Intl.DateTimeFormat('en-US', { timeZone: 'UTC', month: 'long', year: 'numeric' }).format(new Date(Date.UTC(y, m - 1, 1)));
}

/** One meeting of a class: the class, which week it is (1-based), and its Chicago date (YYYY-MM-DD). */
interface Session {
  c: ArtClass;
  n: number;
  date: string;
}
