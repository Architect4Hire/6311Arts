import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { switchMap } from 'rxjs';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { Hero } from '../../shared/hero/hero';

/** Privacy, terms and any other plain page staff publish in WordPress. */
@Component({
  selector: 'app-cms-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero],
  templateUrl: './cms-page.html',
})
export class CmsPage {
  protected site = SITE;
  private api = inject(SiteApi);
  private route = inject(ActivatedRoute);
  protected fallbackTitle = computed(() => this.route.snapshot.data['title'] as string);
  protected page = toSignal(this.route.data.pipe(switchMap((d) => this.api.page(d['pageSlug']))));

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
