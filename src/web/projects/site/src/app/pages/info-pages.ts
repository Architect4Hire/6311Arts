import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Seo } from '../core/seo.service';
import { SITE } from '../core/site';
import { WpApi } from '../core/wp-api.service';
import { MEMBERSHIPS } from '../data/mock-shop';
import { EVENT_PHOTOS, FRONT_PHOTOS, STUDIO_PHOTOS } from '../data/venue-photos';
import { Contact } from '../shared/contact';
import { MoneyPipe } from '../shared/format';
import { Hero } from '../shared/hero';
import { Newsletter } from '../shared/newsletter';
import { Reveal } from '../shared/reveal';

@Component({
  selector: 'app-memberships',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, UiIcon, Reveal, MoneyPipe, RouterLink],
  template: `
    <app-hero title="Become a member" eyebrow="From $100 a year"
      lead="Members save on every class, party with the artists once a year and keep a free gallery free for the whole neighborhood."
      [images]="eventPhotos" />
    <section class="band band--light" id="content">
      <div class="container tiers">
        @for (t of tiers; track t.slug; let i = $index) {
          <article class="tier" [class.tier--featured]="t.featured" [attr.data-tier]="t.name.toLowerCase()" [reveal]="i * 150">
            <h2 class="tier__name">{{ t.name }}</h2>
            <p class="tier__price">{{ t.price | money }}<span>/year</span></p>
            <p class="tier__save">{{ t.discount }}% off every class</p>
            <ul class="tier__perks">
              @for (perk of t.perks; track perk) { <li><ui-icon name="star" [size]="14" [stroke]="2" />{{ perk }}</li> }
            </ul>
            <a class="btn btn-lg" [class.btn-white]="t.featured" [class.btn-dark]="!t.featured" [href]="api.buyUrl(t)">Join {{ t.name }}</a>
          </article>
        }
      </div>
    </section>
    <section class="band band--white text-center">
      <div class="container container--mid" reveal>
        <h2 class="band__title">Why it matters</h2>
        <p class="band__lead">6311arts is a nonprofit. Memberships help keep exhibitions free, pay for free career sessions and art therapy, and keep a home on Clark Street for the artists who live around it.</p>
        <a class="btn btn-dark btn-lg" routerLink="/classes">Browse classes</a>
      </div>
    </section>
  `,
})
export class Memberships {
  protected api = inject(WpApi);
  protected tiers = MEMBERSHIPS;
  /** The annual members' party, which is most of what the page is selling. */
  protected eventPhotos = EVENT_PHOTOS;
  constructor() {
    inject(Seo).set({
      title: 'Memberships: Save on Art Classes & Support a Local Gallery',
      description:
        'Join 6311arts from $100 a year. Members get 5% to 15% off every class and workshop, the annual membership party and 6311arts gear.',
      path: '/memberships',
      breadcrumbs: [{ name: 'Memberships', path: '/memberships' }],
      jsonLd: MEMBERSHIPS.map((t) => ({
        '@type': 'Offer',
        name: `${t.name} membership`,
        price: (t.price / 100).toFixed(2),
        priceCurrency: 'USD',
        description: t.perks.join('; '),
        url: `${SITE.url}/memberships`,
        seller: { '@id': `${SITE.url}/#org` },
      })),
    });
  }
}

@Component({
  selector: 'app-about',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, UiIcon, Reveal, RouterLink, Newsletter],
  template: `
    <app-hero title="Who we are" eyebrow="6311arts · founded in 2025"
      lead="An inclusive art center in a former funeral home on Clark Street." [images]="images" />
    <section class="band band--light text-center" id="content">
      <div class="container container--mid" reveal>
        <h2 class="band__title">Our story</h2>
        <p class="band__lead">
          6311arts opened in 2025 in a 5,000-square-foot former funeral home in Rogers Edge, the corner of Edgewater
          where it meets Rogers Park. Under the building’s original wood ceiling we built a gallery, a classroom and a shop.
        </p>
        <p class="band__lead">
          Our mission is simple: give local artists of all backgrounds a unique and safe space to exhibit and sell their
          work, and back it up with free career development sessions. We teach printmaking, watercolor, mosaic and more.
        </p>
      </div>
    </section>
    <section class="band band--dark">
      <div class="container cols-3 text-center">
        <div class="feature" reveal><ui-icon name="frame" [size]="64" [stroke]="1" /><h3 class="feature__title">The gallery</h3><p>Three rooms of rotating exhibitions, free and open five days a week.</p></div>
        <div class="feature" reveal="150"><ui-icon name="brush" [size]="64" [stroke]="1" /><h3 class="feature__title">The classroom</h3><p>A print studio and workshop space where local artists teach small groups.</p></div>
        <div class="feature" reveal="300"><ui-icon name="cart" [size]="64" [stroke]="1" /><h3 class="feature__title">The shop</h3><p>Hundreds of original works by artists who live within six miles.</p></div>
      </div>
    </section>
    <section class="band band--white text-center">
      <div class="container">
        <h2 class="band__title">Our team</h2>
        <div class="team">
          @for (p of team; track p.name; let i = $index) {
            <div class="team__person" [reveal]="i * 120">
              <div class="team__photo" aria-hidden="true">{{ initials(p.name) }}</div>
              <h3 class="team__name">{{ p.name }}</h3>
              <p class="team__role">{{ p.role }}</p>
            </div>
          }
        </div>
      </div>
    </section>
    <section class="band band--light text-center">
      <div class="container container--mid" reveal>
        <h2 class="band__title">Get involved</h2>
        <p class="btn-row">
          <a class="btn btn-dark btn-lg" routerLink="/memberships">Become a member</a>
          <a class="btn btn-border-dark btn-lg" routerLink="/submit">Submit your work</a>
          <a class="btn btn-border-dark btn-lg" routerLink="/visit">Visit</a>
        </p>
      </div>
    </section>
    <app-newsletter />
  `,
})
export class About {
  /** A room full of people is the story this page tells; show posters are on /exhibitions. */
  protected images = EVENT_PHOTOS;
  protected team = [
    { name: 'Holly King', role: 'Director' },
    { name: 'Ginger Plesha', role: 'Printmaking Manager' },
    { name: 'Michael Winchester', role: 'Gallery Manager' },
    { name: 'Clyde Thurman', role: 'Gallery Assistant Manager' },
  ];
  initials(n: string) {
    return n.split(' ').map((w) => w[0]).join('');
  }
  constructor() {
    inject(Seo).set({
      title: 'About 6311arts | An Inclusive Art Center in Edgewater, Chicago',
      description:
        'Founded in 2025 in a former funeral home on Clark Street, 6311arts is a gallery, classroom and shop giving North Side artists of every background a place to show, sell and teach.',
      path: '/about',
      breadcrumbs: [{ name: 'About', path: '/about' }],
    });
  }
}

@Component({
  selector: 'app-visit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, Contact, UiIcon, Reveal],
  template: `
    <app-hero title="Plan your visit" eyebrow="Free admission"
      [lead]="site.address.street + ', ' + site.address.city + ' · ' + site.address.neighborhood"
      [images]="frontPhotos" />
    <section class="band band--dark" id="content">
      <div class="container cols-3 text-center">
        <div class="feature" reveal><ui-icon name="clock" [size]="56" [stroke]="1" /><h3 class="feature__title">Hours</h3>
          <p>Wed & Sun 11 am – 5 pm<br />Thu – Sat 11 am – 6 pm<br />Mon & Tue by appointment</p></div>
        <div class="feature" reveal="150"><ui-icon name="pin" [size]="56" [stroke]="1" /><h3 class="feature__title">Getting here</h3>
          <p>On Clark Street a block south of Devon Avenue, on the #22 Clark bus line.</p></div>
        <div class="feature" reveal="300"><ui-icon name="heart" [size]="56" [stroke]="1" /><h3 class="feature__title">Always free</h3>
          <p>The gallery and opening receptions are free and open to everyone.</p></div>
      </div>
    </section>
    <div class="map">
      <iframe title="Map to 6311arts" loading="lazy" referrerpolicy="no-referrer-when-downgrade"
        src="https://maps.google.com/maps?q=6311%20N%20Clark%20St%2C%20Chicago%2C%20IL%2060660&z=15&output=embed"></iframe>
    </div>
    <app-contact />
  `,
})
export class Visit {
  protected site = SITE;
  /** The storefront, so visitors recognize the door before they get there. */
  protected frontPhotos = FRONT_PHOTOS;
  constructor() {
    inject(Seo).set({
      title: 'Visit 6311arts | Hours, Directions & Contact',
      description: `Free art gallery at ${SITE.address.street}, Chicago. Open Wed & Sun 11–5, Thu–Sat 11–6, Mon–Tue by appointment. Call ${SITE.phone}.`,
      path: '/visit',
      breadcrumbs: [{ name: 'Visit', path: '/visit' }],
    });
  }
}

@Component({
  selector: 'app-submit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, UiIcon, Reveal],
  template: `
    <app-hero title="Submit your work" eyebrow="North Side artists wanted"
      lead="Teach our classes, show in our galleries, sell in our shop. Submissions are open year-round."
      [images]="studioPhotos" />
    <section class="band band--white" id="content">
      <div class="container cols-3 text-center">
        <div class="feature feature--dark" reveal><ui-icon name="brush" [size]="64" [stroke]="1" /><h3 class="feature__title">Teach</h3>
          <p>Pitch a one-night workshop or a five-week course in your medium.</p></div>
        <div class="feature feature--dark" reveal="150"><ui-icon name="frame" [size]="64" [stroke]="1" /><h3 class="feature__title">Exhibit</h3>
          <p>Solo, group and East and West gallery shows, chosen with intention.</p></div>
        <div class="feature feature--dark" reveal="300"><ui-icon name="cart" [size]="64" [stroke]="1" /><h3 class="feature__title">Sell</h3>
          <p>Put your prints, ceramics, jewelry and paintings in front of neighbors who buy local.</p></div>
      </div>
    </section>
    <section class="band band--dark text-center">
      <div class="container container--mid" reveal>
        <h2 class="band__title">Ready?</h2>
        <p class="band__lead">The form takes about five minutes. Have a few images of your work and a short bio handy.</p>
        <a class="btn btn-white btn-lg" [href]="site.submitFormUrl" target="_blank" rel="noopener">Open the submission form</a>
      </div>
    </section>
  `,
})
export class Submit {
  protected site = SITE;
  protected studioPhotos = STUDIO_PHOTOS;
  constructor() {
    inject(Seo).set({
      title: 'Submit Your Work | Teach, Exhibit or Sell at 6311arts',
      description:
        '6311arts is looking for North Side Chicago artists to teach classes, show in our galleries and sell in our shop. Submissions are accepted on a rolling basis.',
      path: '/submit',
      breadcrumbs: [{ name: 'Submit your work', path: '/submit' }],
    });
  }
}

/** Privacy, terms and any other plain page staff publish in WordPress. */
@Component({
  selector: 'app-cms-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero],
  template: `
    <app-hero [title]="page()?.title ?? fallbackTitle()" />
    <section class="band band--white" id="content">
      <div class="container container--narrow prose">
        @if (page(); as p) {
          <div [innerHTML]="p.html"></div>
        } @else {
          <p>This page is being updated. For questions in the meantime, email <a [href]="'mailto:' + site.email">{{ site.email }}</a>.</p>
        }
      </div>
    </section>
  `,
})
export class CmsPage {
  protected site = SITE;
  private api = inject(WpApi);
  private route = inject(ActivatedRoute);
  protected fallbackTitle = computed(() => this.route.snapshot.data['title'] as string);
  protected page = toSignal(this.route.data.pipe(switchMap((d) => this.api.page(d['wpSlug']))));

  constructor() {
    const seo = inject(Seo);
    effect(() =>
      seo.set({
        title: this.page()?.title ?? this.fallbackTitle(),
        description: `${this.fallbackTitle()} for ${SITE.name}, ${SITE.address.street}, Chicago.`,
        path: '/' + this.route.snapshot.url.map((u) => u.path).join('/'),
      }),
    );
  }
}
