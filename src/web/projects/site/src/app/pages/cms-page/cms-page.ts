import { ChangeDetectionStrategy, Component, computed, effect, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { switchMap } from 'rxjs';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { Hero } from '../../shared/hero/hero';

/** Privacy, terms and any other plain page staff publish in the CMS. */
@Component({
  selector: 'app-cms-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero],
  templateUrl: './cms-page.html',
})
export class CmsPage {
  protected site = SITE;
  private api = inject(SiteApi);

  /** The route's `data.pageSlug`, bound by the router (`withComponentInputBinding`), or the preview page's (ADR 0063). */
  readonly pageSlug = input.required<string>();
  /** The route's `data.fallbackTitle`: what the banner says while the page is being read, or if there is none. */
  readonly fallbackTitle = input('');

  protected page = toSignal(
    toObservable(this.pageSlug).pipe(switchMap((slug) => this.api.page(slug))),
  );
  protected heading = computed(() => this.page()?.title ?? this.fallbackTitle());

  constructor() {
    const seo = inject(Seo);
    effect(() =>
      seo.set({
        title: this.heading(),
        // The route's own words, not the CMS title: the description of /privacy and /terms is unchanged by an
        // edit to the page's heading (ADR 0063).
        description: `${this.fallbackTitle()} for ${SITE.name}, ${SITE.address.street}, Chicago.`,
        path: `/${this.pageSlug()}`,
      }),
    );
  }
}
