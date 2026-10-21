import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Seo } from '../core/seo.service';
import { SITE } from '../core/site';
import { SiteApi } from '../services/site-api.service';
import { slugify } from '../data/mock-shop';
import { STUDIO_PHOTOS } from '../data/venue-photos';
import { ArtImage } from '../shared/art-image';
import { ClassCard, ProductCard } from '../shared/cards';
import { Hero } from '../shared/hero';
import { Reveal } from '../shared/reveal';

/**
 * One teacher, everything the gallery wants to say about them: who they are, what they teach,
 * what is on the calendar with them now, and the work they sell here if they also show in the shop.
 *
 * Classes only carry an instructor's name, so the name is slugified into this route and matched
 * against the instructor feed. A name with no profile yet still gets a page built from their classes.
 */
@Component({
  selector: 'app-instructor-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ArtImage, ClassCard, ProductCard, UiIcon, Reveal, RouterLink],
  template: `
    <app-hero [title]="name()" [eyebrow]="person()?.medium ?? 'Instructor at 6311arts'" [images]="studioPhotos" />

    <section class="band band--white" id="content">
      <div class="container detail">
        <div class="detail__media">
          @if (person()?.image) {
            <app-art-image [src]="person()!.image" [alt]="name()" ratio="1 / 1" [priority]="true" />
          } @else {
            <div class="tutor__photo" aria-hidden="true">{{ initials() }}</div>
          }
        </div>
        <div class="detail__info">
          <p class="crumbs"><a routerLink="/classes">Classes</a> · Instructor</p>
          <h2 class="detail__title">{{ name() }}</h2>
          @if (person()?.bio) {
            <p class="detail__lead">{{ person()!.bio }}</p>
          } @else {
            <p class="detail__lead">
              {{ name() }} teaches at 6311arts. We’re still writing their profile — in the meantime, their upcoming
              classes are below.
            </p>
          }
          <ul class="facts">
            @if (person()?.medium) {
              <li><ui-icon name="brush" [size]="22" /><span>Teaches <strong>{{ person()!.medium }}</strong></span></li>
            }
            <li><ui-icon name="clock" [size]="22" />
              <span>@if (classes().length) {
                <strong>{{ classes().length }}</strong> {{ classes().length === 1 ? 'class' : 'classes' }} on the calendar
              } @else { Nothing scheduled right now }</span></li>
            @if (person()?.neighborhood) {
              <li><ui-icon name="pin" [size]="22" /><span>Based in {{ person()!.neighborhood }}</span></li>
            }
            @if (artist(); as a) {
              <li><ui-icon name="cart" [size]="22" />
                <span>Also sells in the shop — <a class="linkish" [routerLink]="['/artists', a.slug]">see their work</a></span></li>
            }
            @if (person()?.website) {
              <li><ui-icon name="arrow" [size]="22" />
                <span><a class="linkish" [href]="person()!.website" target="_blank" rel="noopener">{{ person()!.website }}</a></span></li>
            }
            @if (person()?.instagram) {
              <li><ui-icon name="instagram" [size]="22" />
                <span><a class="linkish" [href]="'https://instagram.com/' + person()!.instagram" target="_blank" rel="noopener">&#64;{{ person()!.instagram }}</a></span></li>
            }
          </ul>
          <p class="btn-row" style="justify-content: flex-start">
            <a class="btn btn-dark" routerLink="/classes">All classes</a>
            @if (artist(); as a) {
              <a class="btn btn-border-dark" [routerLink]="['/artists', a.slug]">Shop their work</a>
            }
          </p>
        </div>
      </div>
    </section>

    @if (classes().length) {
      <section class="band band--light">
        <div class="container">
          <h2 class="band__title text-center">Coming up with {{ firstName() }}</h2>
          <div class="class-list">
            @for (c of classes(); track c.id) { <app-class-card [item]="c" reveal /> }
          </div>
        </div>
      </section>
    }

    @if (works().length) {
      <section class="band band--white">
        <div class="container">
          <h2 class="band__title text-center">{{ firstName() }} in the shop</h2>
          <div class="grid-products">
            @for (p of works(); track p.id) { <app-product-card [item]="p" /> }
          </div>
        </div>
      </section>
    }

    <section class="band band--dark text-center">
      <div class="container container--mid" reveal>
        <h2 class="band__title">Teach a class here</h2>
        <p class="band__lead">We’re always looking for North Side artists to lead a workshop or a five-week course.</p>
        <a class="btn btn-white btn-lg" routerLink="/artists/submit">Pitch a class</a>
      </div>
    </section>
  `,
})
export class InstructorDetail {
  private api = inject(SiteApi);
  private route = inject(ActivatedRoute);
  protected studioPhotos = STUDIO_PHOTOS;

  private slug = toSignal(this.route.paramMap.pipe(map((p) => p.get('slug')!)), { requireSync: true });
  private all = toSignal(this.api.instructors(), { initialValue: [] });
  private allClasses = toSignal(this.api.classes(), { initialValue: [] });
  private artists = toSignal(this.api.artists(), { initialValue: [] });

  protected person = computed(() => this.all().find((i) => i.slug === this.slug()));
  /** Classes keep the instructor's name, so match on the slugified name rather than an id. */
  protected classes = computed(() => this.allClasses().filter((c) => c.instructor && slugify(c.instructor) === this.slug()));
  /** Fall back to the name carried by their classes, then to the slug itself. */
  protected name = computed(
    () =>
      this.person()?.name ??
      this.classes()[0]?.instructor ??
      this.slug().replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase()),
  );
  protected firstName = computed(() => this.name().split(' ')[0]);
  protected initials = computed(() => this.name().split(' ').map((w) => w[0]).join('').slice(0, 3));
  protected artist = computed(() => {
    const want = this.person()?.artistSlug ?? this.slug();
    return this.artists().find((a) => a.slug === want);
  });
  protected works = toSignal(
    this.route.paramMap.pipe(
      switchMap((p) => this.api.products({ artist: p.get('slug')!, limit: 8 })),
      map((r) => r.items),
    ),
    { initialValue: [] },
  );

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const name = this.name();
      const path = `/instructors/${this.slug()}`;
      const teaches = this.person()?.medium ?? this.classes()[0]?.type;
      seo.set({
        title: `${name} | Art Instructor in Chicago`,
        description:
          `${name} teaches${teaches ? ` ${teaches.toLowerCase()}` : ''} at 6311arts in Edgewater, Chicago.` +
          `${this.classes().length ? ` ${this.classes().length} upcoming classes.` : ''}`,
        path,
        image: this.person()?.image ?? this.classes()[0]?.image,
        breadcrumbs: [{ name: 'Classes', path: '/classes' }, { name, path }],
        jsonLd: [
          {
            '@type': 'Person',
            name,
            jobTitle: 'Instructor',
            description: this.person()?.bio,
            url: SITE.url + path,
            worksFor: { '@id': `${SITE.url}/#org` },
          },
        ],
      });
    });
  }
}

/** The whole teaching roster, for anyone who lands on /instructors. */
@Component({
  selector: 'app-instructors',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, Reveal, RouterLink],
  template: `
    <app-hero title="Who teaches here" eyebrow="Artists first, teachers second"
      lead="Every class is led by someone who makes the work themselves, most of them from the neighborhood."
      [images]="studioPhotos" />
    <section class="band band--white" id="content">
      <div class="container">
        <div class="team">
          @for (i of roster(); track i.slug; let n = $index) {
            <a class="team__person" [routerLink]="['/instructors', i.slug]" [reveal]="n * 80">
              <div class="team__photo" aria-hidden="true">{{ initials(i.name) }}</div>
              <h2 class="team__name">{{ i.name }}</h2>
              <p class="team__role">{{ i.medium ?? 'Instructor' }}</p>
            </a>
          }
        </div>
      </div>
    </section>
    <section class="band band--dark text-center">
      <div class="container container--mid" reveal>
        <h2 class="band__title">Want to teach a class?</h2>
        <a class="btn btn-white btn-lg" routerLink="/artists/submit">Pitch a class</a>
      </div>
    </section>
  `,
})
export class Instructors {
  private api = inject(SiteApi);
  protected studioPhotos = STUDIO_PHOTOS;
  private all = toSignal(this.api.instructors(), { initialValue: [] });
  private classes = toSignal(this.api.classes(), { initialValue: [] });

  /** Anyone with a profile, plus anyone teaching a class who has no profile yet. */
  protected roster = computed(() => {
    const bySlug = new Map(this.all().map((i) => [i.slug, i]));
    for (const c of this.classes()) {
      if (c.instructor && !bySlug.has(slugify(c.instructor))) {
        bySlug.set(slugify(c.instructor), { slug: slugify(c.instructor), name: c.instructor });
      }
    }
    return [...bySlug.values()].sort((a, b) => a.name.localeCompare(b.name));
  });

  protected initials(n: string) {
    return n.split(' ').map((w) => w[0]).join('').slice(0, 3);
  }

  constructor() {
    inject(Seo).set({
      title: 'Our Instructors | Local Artists Teaching in Edgewater, Chicago',
      description:
        'Meet the working artists who teach screen printing, drawing, mixed media and more at 6311arts on Chicago’s North Side.',
      path: '/instructors',
      breadcrumbs: [{ name: 'Instructors', path: '/instructors' }],
    });
  }
}
