import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { type IconName, UiIcon } from '@6311/ui';
import { Seo, eventLd, exhibitionLd } from '../core/seo.service';
import { SITE } from '../core/site';
import { WpApi } from '../core/wp-api.service';
import { MEMBERSHIPS } from '../data/mock-shop';
import { EVENT_PHOTOS, GALLERY_PHOTOS, HERO_PHOTO, STUDIO_PHOTOS } from '../data/venue-photos';
import { ClassCard, ProductCard, exhibitionStatus } from '../shared/cards';
import { Contact } from '../shared/contact';
import { MoneyPipe, WhenPipe } from '../shared/format';
import { Hero } from '../shared/hero';
import { Newsletter } from '../shared/newsletter';
import { Reveal } from '../shared/reveal';

@Component({
  selector: 'app-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Hero, UiIcon, Reveal, ClassCard, ProductCard, Newsletter, Contact, WhenPipe, MoneyPipe],
  templateUrl: './home.html',
})
export class Home {
  private api = inject(WpApi);

  protected exhibitions = toSignal(this.api.exhibitions(), { initialValue: [] });
  protected classes = toSignal(this.api.classes(), { initialValue: [] });
  protected products = toSignal(this.api.products({ perPage: 8 }).pipe(map((p) => p.items)), { initialValue: [] });
  protected tiers = MEMBERSHIPS;

  /**
   * The opening slideshow and the two full-bleed bands below it. Rooms and openings rather than show posters:
   * a portrait poster cropped to a full-height banner shows only its middle.
   */
  protected heroImages = [HERO_PHOTO, EVENT_PHOTOS[0], STUDIO_PHOTOS[0], GALLERY_PHOTOS[0], EVENT_PHOTOS[1]];

  /** The two full-bleed bands lower down: the studio behind "what we do", a packed opening behind memberships. */
  protected pillarPhoto = STUDIO_PHOTOS[1];
  protected memberPhoto = EVENT_PHOTOS[2];

  /** What's on the walls now, or the next show if we're between exhibitions. */
  protected featured = computed(() => {
    const list = this.exhibitions();
    const now = list.find((e) => exhibitionStatus(e) === 'Now on view');
    const next = list.filter((e) => exhibitionStatus(e) === 'Coming up').sort((a, b) => a.start.localeCompare(b.start))[0];
    return now ?? next;
  });
  protected featuredIsNext = computed(() => {
    const f = this.featured();
    return !!f && exhibitionStatus(f) === 'Coming up';
  });

  protected upcoming = computed(() => this.classes().slice(0, 4));

  protected pillars: { icon: IconName; title: string; text: string }[] = [
    { icon: 'brush', title: 'Artist-focused', text: 'Every resident artist lives within six miles of our door. We put local work on the walls, in the shop and at the front of the classroom.' },
    { icon: 'people', title: 'Committed to community', text: 'Our programs and partnerships come from what Rogers Edge asks for, from high-school shows to free art therapy.' },
    { icon: 'infinity', title: 'Moved by meaning', text: 'Exhibitions, teachers and classes are chosen with intention. Every show has a reason to be here, now.' },
    { icon: 'heart', title: 'Room for everyone', text: 'First-timers, working artists and neighbors who just want to look. No gatekeeping, no velvet rope.' },
  ];

  protected programs: { icon: IconName; title: string; text: string }[] = [
    { icon: 'brush', title: 'Community workshops', text: 'Screen printing, scratchboard, ofrendas and more, in small hands-on groups.' },
    { icon: 'chat', title: 'Talks & lectures', text: 'Artists and historians on the ideas behind the work, from activism to process.' },
    { icon: 'briefcase', title: 'Free career sessions', text: 'Pricing, portfolios and grant writing for working artists, at no cost.' },
    { icon: 'leaf', title: 'Free art therapy', text: 'Guided sessions that use making as a way to rest, process and connect.' },
  ];

  protected stats = [
    { n: '50+', label: 'Local artists' },
    { n: '780+', label: 'Original works for sale' },
    { n: '6 mi', label: 'Artist radius' },
    { n: '5,000', label: 'Sq ft under the original wood ceiling' },
  ];

  constructor() {
    const seo = inject(Seo);
    // Re-runs when classes and exhibitions arrive, so the server-rendered head carries Event rich results.
    effect(() => {
      const f = this.featured();
      seo.set({
        title: '6311arts | Gallery, Art Classes & Local Art Shop in Edgewater, Chicago',
        description: SITE.description,
        path: '/',
        jsonLd: [
          ...this.classes().slice(0, 6).map((c) => eventLd(c, `${SITE.url}/classes/${c.slug}`)),
          ...(f ? [exhibitionLd(f)] : []),
        ],
      });
    });
  }
}
