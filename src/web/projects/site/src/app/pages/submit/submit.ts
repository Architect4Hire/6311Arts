import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { STUDIO_PHOTOS } from '../../data/venue-photos';
import { Hero } from '../../shared/hero/hero';
import { Reveal } from '../../shared/reveal';

@Component({
  selector: 'app-submit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, UiIcon, Reveal, RouterLink],
  templateUrl: './submit.html',
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
