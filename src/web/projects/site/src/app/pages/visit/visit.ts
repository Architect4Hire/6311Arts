import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { UiIcon } from '@6311/ui';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { FRONT_PHOTOS } from '../../data/venue-photos';
import { Contact } from '../../shared/contact/contact';
import { Hero } from '../../shared/hero/hero';
import { Reveal } from '../../shared/reveal';

@Component({
  selector: 'app-visit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, Contact, UiIcon, Reveal],
  templateUrl: './visit.html',
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
