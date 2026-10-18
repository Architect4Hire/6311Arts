import { ChangeDetectionStrategy, Component, RESPONSE_INIT, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Seo } from '../core/seo.service';
import { Hero } from '../shared/hero';

@Component({
  selector: 'app-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, RouterLink],
  template: `
    <app-hero title="Nothing on this wall" eyebrow="404" lead="The page you’re looking for has moved or sold out.">
      <a class="btn btn-border btn-lg" routerLink="/">Home</a>
      <a class="btn btn-white btn-lg" routerLink="/classes">See classes</a>
    </app-hero>
  `,
})
export class NotFound {
  constructor() {
    // Real 404 status for crawlers when rendered on the server.
    const init = inject(RESPONSE_INIT, { optional: true });
    if (init) init.status = 404;
    inject(Seo).set({ title: 'Page not found', description: 'This page could not be found.', path: '/404', noindex: true });
  }
}
