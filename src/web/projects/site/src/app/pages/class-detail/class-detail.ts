import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map, switchMap } from 'rxjs';
import { UiIcon } from '@6311/ui';
import { Seo, eventLd } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { LegacyStore } from '../../core/legacy-store';
import { slugify } from '../../data/mock-shop';
import { STUDIO_PHOTOS } from '../../data/venue-photos';
import { ArtImage } from '../../shared/media/art-image/art-image';
import { ClassCard } from '../../shared/cards/class-card/class-card';
import { MoneyPipe, WhenPipe } from '../../shared/format';
import { Hero } from '../../shared/hero/hero';
import { NotFound } from '../not-found/not-found';

@Component({
  selector: 'app-class-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, ArtImage, ClassCard, UiIcon, MoneyPipe, WhenPipe, RouterLink, NotFound],
  templateUrl: './class-detail.html',
})
export class ClassDetail {
  private api = inject(SiteApi);
  protected store = inject(LegacyStore);
  protected site = SITE;
  private tiers = toSignal(this.api.memberships(), { initialValue: [] });
  /** Instructor profiles are keyed on the slugified name, since classes carry only the name. */
  protected instructorSlug = slugify;
  /** The flyer is shown whole further down the page, so the banner shows the room the class runs in. */
  protected studioPhotos = STUDIO_PHOTOS;
  private slug = toSignal(inject(ActivatedRoute).paramMap.pipe(map((p) => p.get('slug')!)), {
    requireSync: true,
  });

  protected all = toSignal(
    inject(ActivatedRoute).paramMap.pipe(switchMap(() => this.api.classes())),
  );
  protected loaded = computed(() => this.all() !== undefined);
  protected cls = computed(() => this.all()?.find((c) => c.slug === this.slug()));
  protected more = computed(() =>
    (this.all() ?? []).filter((c) => c.slug !== this.slug()).slice(0, 3),
  );
  /** The most a member pays: the price less the smallest tier discount. */
  protected memberPrice = computed(() => {
    const discounts = this.tiers().map((t) => t.discount);
    return discounts.length
      ? Math.round(((this.cls()?.price ?? 0) * (100 - Math.min(...discounts))) / 100)
      : undefined;
  });
  /** The cheapest tier Square has priced. */
  protected joinFrom = computed(() => {
    const prices = this.tiers().flatMap((t) => (t.price == null ? [] : [t.price]));
    return prices.length ? Math.min(...prices) : undefined;
  });

  constructor() {
    const seo = inject(Seo);
    effect(() => {
      const c = this.cls();
      if (!c) return;
      const path = `/classes/${c.slug}`;
      seo.set({
        title: `${c.title} | Art Class in Chicago`,
        description: `${c.summary} ${c.sessions > 1 ? `${c.sessions}-week course. ` : ''}${c.instructor ? `With ${c.instructor}. ` : ''}At 6311arts, Edgewater.`,
        path,
        image: c.image,
        type: 'event',
        breadcrumbs: [
          { name: 'Classes', path: '/classes' },
          { name: c.title, path },
        ],
        jsonLd: [eventLd(c, SITE.url + path)],
      });
    });
  }
}
