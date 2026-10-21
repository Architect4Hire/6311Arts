import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { slugify } from '../../data/mock-shop';
import { STUDIO_PHOTOS } from '../../data/venue-photos';
import { ArtImage } from '../../shared/media/art-image/art-image';
import { ClassCard } from '../../shared/cards/class-card/class-card';
import { ProductCard } from '../../shared/cards/product-card/product-card';
import { Hero } from '../../shared/hero/hero';
import { Reveal } from '../../shared/reveal';

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
  templateUrl: './instructor-detail.html',
})
export class InstructorDetail {
  private api = inject(SiteApi);
  private route = inject(ActivatedRoute);
  protected studioPhotos = STUDIO_PHOTOS;

  private slug = toSignal(this.route.paramMap.pipe(map((p) => p.get('slug')!)), {
    requireSync: true,
  });
  private all = toSignal(this.api.instructors(), { initialValue: [] });
  private allClasses = toSignal(this.api.classes(), { initialValue: [] });
  private artists = toSignal(this.api.artists(), { initialValue: [] });

  protected person = computed(() => this.all().find((i) => i.slug === this.slug()));
  /** Classes keep the instructor's name, so match on the slugified name rather than an id. */
  protected classes = computed(() =>
    this.allClasses().filter((c) => c.instructor && slugify(c.instructor) === this.slug()),
  );
  /** Fall back to the name carried by their classes, then to the slug itself. */
  protected name = computed(
    () =>
      this.person()?.name ??
      this.classes()[0]?.instructor ??
      this.slug()
        .replace(/-/g, ' ')
        .replace(/\b\w/g, (c) => c.toUpperCase()),
  );
  protected firstName = computed(() => this.name().split(' ')[0]);
  protected initials = computed(() =>
    this.name()
      .split(' ')
      .map((w) => w[0])
      .join('')
      .slice(0, 3),
  );
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
        breadcrumbs: [
          { name: 'Classes', path: '/classes' },
          { name, path },
        ],
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
