import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { Seo } from '../../core/seo.service';
import { EVENT_PHOTOS } from '../../data/venue-photos';
import { Hero } from '../../shared/hero/hero';
import { Newsletter } from '../../shared/forms/newsletter/newsletter';
import { Reveal } from '../../shared/reveal';

@Component({
  selector: 'app-about',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, UiIcon, Reveal, RouterLink, Newsletter],
  templateUrl: './about.html',
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
    return n
      .split(' ')
      .map((w) => w[0])
      .join('');
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
