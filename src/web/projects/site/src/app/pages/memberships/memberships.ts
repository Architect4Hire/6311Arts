import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { UiIcon } from '@6311/ui';
import { Seo } from '../../core/seo.service';
import { SITE } from '../../core/site';
import { SiteApi } from '../../services/site-api.service';
import { LegacyStore } from '../../core/legacy-store';
import { EVENT_PHOTOS } from '../../data/venue-photos';
import { MoneyPipe } from '../../shared/format';
import { Hero } from '../../shared/hero/hero';
import { Reveal } from '../../shared/reveal';

@Component({
  selector: 'app-memberships',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Hero, UiIcon, Reveal, MoneyPipe, RouterLink],
  templateUrl: './memberships.html',
})
export class Memberships {
  protected store = inject(LegacyStore);
  /** Every published tier, from the CMS (ADR 0040); prices are Square's record. */
  protected tiers = toSignal(inject(SiteApi).memberships(), { initialValue: [] });
  /** The cheapest tier Square has priced: the price is Square's, so the page never states one of its own. */
  protected joinFrom = computed(() => {
    const prices = this.tiers().flatMap((t) => (t.price == null ? [] : [t.price]));
    return prices.length ? Math.min(...prices) : undefined;
  });
  /** The annual members' party, which is most of what the page is selling. */
  protected eventPhotos = EVENT_PHOTOS;
  constructor() {
    const seo = inject(Seo);
    const money = new MoneyPipe();
    // Re-runs when the tiers arrive, so the server-rendered head carries an Offer for each priced one.
    effect(() => {
      const from = this.joinFrom();
      const discounts = this.tiers().map((t) => t.discount);
      const savings = discounts.length
        ? `${Math.min(...discounts)}% to ${Math.max(...discounts)}% off`
        : 'money off';
      seo.set({
        title: 'Memberships: Save on Art Classes & Support a Local Gallery',
        description:
          `Join 6311arts${from != null ? ` from ${money.transform(from)} a year` : ''}. ` +
          `Members get ${savings} every class and workshop, the annual membership party and 6311arts gear.`,
        path: '/memberships',
        breadcrumbs: [{ name: 'Memberships', path: '/memberships' }],
        jsonLd: this.tiers().flatMap((t) =>
          t.price == null
            ? []
            : [
                {
                  '@type': 'Offer',
                  name: `${t.name} membership`,
                  price: (t.price / 100).toFixed(2),
                  priceCurrency: 'USD',
                  description: t.perks.join('; '),
                  url: `${SITE.url}/memberships`,
                  seller: { '@id': `${SITE.url}/#org` },
                },
              ],
        ),
      });
    });
  }
}
