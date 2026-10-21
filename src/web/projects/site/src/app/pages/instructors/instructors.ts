import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Seo } from '../../core/seo.service';
import { SiteApi } from '../../services/site-api.service';
import { slugify } from '../../data/mock-shop';
import { STUDIO_PHOTOS } from '../../data/venue-photos';
import { Hero } from '../../shared/hero/hero';
import { Reveal } from '../../shared/reveal';

/** The whole teaching roster, for anyone who lands on /instructors. */
@Component({
  selector: 'app-instructors',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, Reveal, RouterLink],
  templateUrl: './instructors.html',
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
    return n
      .split(' ')
      .map((w) => w[0])
      .join('')
      .slice(0, 3);
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
