import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Seo, eventLd } from '../core/seo.service';
import { SITE } from '../core/site';
import { SiteApi } from '../services/site-api.service';
import { LegacyStore } from '../core/legacy-store';
import { slugify } from '../data/mock-shop';
import { STUDIO_PHOTOS } from '../data/venue-photos';
import { ArtImage } from '../shared/art-image';
import { ClassCard } from '../shared/cards';
import { MoneyPipe, WhenPipe } from '../shared/format';
import { Hero } from '../shared/hero';
import { NotFound } from './not-found';

@Component({
  selector: 'app-class-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ArtImage, ClassCard, UiIcon, MoneyPipe, WhenPipe, RouterLink, NotFound],
  template: `
    @if (cls(); as c) {
      <app-hero [title]="c.title" [eyebrow]="c.type + ' · ' + c.level" [images]="studioPhotos" />
      <section class="band band--white" id="content">
        <div class="container detail">
          <div class="detail__media">
            <app-art-image
              [src]="c.image"
              [alt]="c.title + ' flyer'"
              ratio="1 / 1"
              [priority]="true"
            />
          </div>
          <div class="detail__info">
            <p class="eyebrow eyebrow--dark">
              {{ c.sessions > 1 ? c.sessions + '-week course' : 'One session' }}
            </p>
            <h2 class="detail__title">{{ c.title }}</h2>
            <p class="detail__lead">{{ c.summary }}</p>
            <ul class="facts">
              <li>
                <ui-icon name="clock" [size]="22" />
                <span
                  ><strong>{{ c.sessions > 1 ? 'Starts ' : '' }}{{ c.start | when: 'long' }}</strong
                  ><br />
                  {{ c.cadence ? c.cadence + ', ' : '' }}{{ c.start | when: 'time' : c.end }}</span
                >
              </li>
              @if (c.instructor) {
                <li>
                  <ui-icon name="people" [size]="22" />
                  <span
                    >With
                    <a class="linkish" [routerLink]="['/instructors', instructorSlug(c.instructor)]"
                      ><strong>{{ c.instructor }}</strong></a
                    ></span
                  >
                </li>
              }
              <li>
                <ui-icon name="pin" [size]="22" /><span
                  >{{ site.name }}, {{ site.address.street }}</span
                >
              </li>
              @if (c.spotsLeft != null) {
                <li>
                  <ui-icon name="ticket" [size]="22" />
                  <span
                    ><strong>{{
                      c.spotsLeft === 0
                        ? 'Sold out'
                        : c.spotsLeft + ' of ' + c.capacity + ' spots left'
                    }}</strong></span
                  >
                </li>
              }
            </ul>
            <div class="buy">
              @if (c.price != null) {
                <span class="buy__price">{{ c.price | money }}</span>
              } @else {
                <!-- Square hasn't priced the run yet (ADR 0042, 0046). -->
                <p class="buy__note buy__tba">Price to be announced</p>
              }
              @if (c.spotsLeft === 0) {
                <a class="btn btn-border-dark btn-lg" routerLink="/classes">Find another date</a>
              } @else if (c.price != null) {
                <!-- Until Square has priced a run there is nothing to reserve (ADR 0042). -->
                <a class="btn btn-dark btn-lg" [href]="store.buyUrl(c)">{{
                  c.price === 0 ? 'RSVP free' : 'Reserve your spot'
                }}</a>
              }
            </div>
            @if ((c.price ?? 0) > 0 && memberPrice() != null && joinFrom() != null) {
              <p class="buy__note">
                Members pay {{ memberPrice() | money }} or less.
                <a routerLink="/memberships">Join from {{ joinFrom() | money }}/yr</a>.
              </p>
            }
          </div>
        </div>
      </section>
      @if (more().length) {
        <section class="band band--light">
          <div class="container">
            <h2 class="band__title text-center">More coming up</h2>
            <div class="class-list">
              @for (m of more(); track m.id) {
                <app-class-card [item]="m" />
              }
            </div>
          </div>
        </section>
      }
    } @else if (loaded()) {
      <app-not-found />
    }
  `,
})
export class ClassDetail {
  private api = inject(SiteApi);
  protected store = inject(LegacyStore);
  protected site = SITE;
  private tiers = toSignal(this.api.memberships(), { initialValue: [] });
  /** Instructor profiles are keyed on the slugified name, since classes carry only the name. */
  protected instructorSlug = slugify;
  /** The flyer is shown whole further down the page, so the banner shows the room the class runs in. */
  protected studioPhotos = STUDIO_PHOTOS;
  private slug = toSignal(inject(ActivatedRoute).paramMap.pipe(map((p) => p.get('slug')!)), {
    requireSync: true,
  });

  protected all = toSignal(
    inject(ActivatedRoute).paramMap.pipe(switchMap(() => this.api.classes())),
  );
  protected loaded = computed(() => this.all() !== undefined);
  protected cls = computed(() => this.all()?.find((c) => c.slug === this.slug()));
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
      const c = this.cls();
      if (!c) return;
      const path = `/classes/${c.slug}`;
      seo.set({
        title: `${c.title} | Art Class in Chicago`,
        description: `${c.summary} ${c.sessions > 1 ? `${c.sessions}-week course. ` : ''}${c.instructor ? `With ${c.instructor}. ` : ''}At 6311arts, Edgewater.`,
        path,
        image: c.image,
        type: 'event',
        breadcrumbs: [
          { name: 'Classes', path: '/classes' },
          { name: c.title, path },
        ],
        jsonLd: [eventLd(c, SITE.url + path)],
      });
    });
  }
}
