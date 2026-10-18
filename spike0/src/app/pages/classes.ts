import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ClassType } from '../core/models';
import { Seo, eventLd } from '../core/seo.service';
import { SITE } from '../core/site';
import { WpApi } from '../core/wp-api.service';
import { ClassCard } from '../shared/cards';
import { WhenPipe } from '../shared/format';
import { Hero } from '../shared/hero';
import { Icon } from '../shared/icon';
import { Newsletter } from '../shared/newsletter';
import { Reveal } from '../shared/reveal';

/** The calendar: every upcoming class grouped by month, filterable by medium and level. Filters live in the URL. */
@Component({
  selector: 'app-classes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ClassCard, Newsletter, RouterLink, Icon, Reveal],
  template: `
    <app-hero title="Classes, workshops & events" eyebrow="Taught by local artists"
      lead="Small groups, hands-on, and open to first-timers. Pick a night and make something."
      [images]="images()" />

    <section class="band band--white" id="content">
      <div class="container">
        <div class="filters" role="toolbar" aria-label="Filter classes">
          @for (t of types(); track t) {
            <button type="button" class="chip" [class.is-on]="type() === t" (click)="setType(t)" [attr.aria-pressed]="type() === t">{{ t }}</button>
          }
          <span class="filters__sep"></span>
          @for (l of levels; track l) {
            <button type="button" class="chip chip--ghost" [class.is-on]="level() === l" (click)="setLevel(l)" [attr.aria-pressed]="level() === l">{{ l }}</button>
          }
        </div>

        @for (g of grouped(); track g.month) {
          <h2 class="month" reveal>{{ g.month }}</h2>
          <div class="class-list class-list--full">
            @for (c of g.items; track c.id) {
              <app-class-card [item]="c" reveal />
            }
          </div>
        } @empty {
          <p class="empty">Nothing on the calendar matches that yet. <button class="linklike" (click)="reset()">Show everything</button> or get notified below when new classes drop.</p>
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
  protected levels = ['Beginner', 'Intermediate'];

  protected types = computed(() => ['All', ...new Set(this.all().map((c) => c.type))] as (ClassType | 'All')[]);
  protected images = computed(() => this.all().map((c) => c.image));

  protected grouped = computed(() => {
    const list = this.all().filter(
      (c) => (this.type() === 'All' || c.type === this.type()) && (!this.level() || c.level === this.level() || c.level === 'All levels'),
    );
    const by = new Map<string, typeof list>();
    const fmt = new WhenPipe();
    for (const c of list) {
      const key = `${fmt.transform(c.start, 'mon')} ${c.start.slice(0, 4)}`.replace(/^(\w+)/, (m) => MONTHS[m] ?? m);
      by.set(key, [...(by.get(key) ?? []), c]);
    }
    return [...by].map(([month, items]) => ({ month, items }));
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
  private nav(queryParams: Record<string, string | null>) {
    this.router.navigate([], { queryParams, queryParamsHandling: 'merge', replaceUrl: true });
  }
}

const MONTHS: Record<string, string> = {
  Jan: 'January', Feb: 'February', Mar: 'March', Apr: 'April', May: 'May', Jun: 'June',
  Jul: 'July', Aug: 'August', Sep: 'September', Oct: 'October', Nov: 'November', Dec: 'December',
};
