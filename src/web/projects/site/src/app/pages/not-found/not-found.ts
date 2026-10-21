import { ChangeDetectionStrategy, Component, RESPONSE_INIT, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Seo } from '../../core/seo.service';
import { Hero } from '../../shared/hero/hero';

@Component({
  selector: 'app-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, RouterLink],
  templateUrl: './not-found.html',
})
export class NotFound {
  constructor() {
    // Real 404 status for crawlers when rendered on the server.
    const init = inject(RESPONSE_INIT, { optional: true });
    if (init) init.status = 404;
    inject(Seo).set({
      title: 'Page not found',
      description: 'This page could not be found.',
      path: '/404',
      noindex: true,
    });
  }
}
