import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { MembershipTier } from '../core/models';
import { SiteApi } from '../services/site-api.service';
import { Memberships } from './info-pages';

const TIERS: MembershipTier[] = [
  {
    id: 't1',
    slug: 'gray-membership',
    name: 'Gray',
    price: 10000,
    discount: 5,
    perks: ['5% off every class & workshop', '6311arts tote'],
  },
  {
    id: 't2',
    slug: 'black-membership',
    name: 'Black',
    price: 30000,
    discount: 10,
    featured: true,
    perks: ['10% off every class & workshop'],
  },
  {
    id: 't3',
    slug: 'blue-membership',
    name: 'Blue',
    price: undefined,
    discount: 15,
    perks: ['15% off every class & workshop'],
  },
];

async function setup(tiers = TIERS) {
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter([{ path: 'memberships', component: Memberships }]),
      { provide: SiteApi, useValue: { memberships: () => of(tiers) } },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl('/memberships');
  const element = harness.routeNativeElement!;
  const tier = (name: string) => element.querySelector<HTMLElement>(`.tier[data-tier="${name}"]`)!;
  const jsonLd = () =>
    [...TestBed.inject(DOCUMENT).head.querySelectorAll('script[type="application/ld+json"]')].map(
      (s) => JSON.parse(s.textContent ?? '{}') as Record<string, unknown>,
    );
  return { element, tier, jsonLd };
}

describe('Memberships', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows every published tier from the CMS, in the order staff set', async () => {
    const { element } = await setup();
    expect([...element.querySelectorAll('.tier__name')].map((h) => h.textContent)).toEqual([
      'Gray',
      'Black',
      'Blue',
    ]);
  });

  it('prices each tier with Square’s figure and joins through WooCommerce until Phase 5', async () => {
    const { tier } = await setup();
    expect(tier('black').querySelector('.tier__price')?.textContent).toBe('$300/year');
    expect(tier('black').classList).toContain('tier--featured');
    expect(tier('gray').querySelector('.tier__perks')?.textContent).toContain('6311arts tote');
    expect(tier('gray').querySelector('a')?.getAttribute('href')).toBe(
      'https://6311arts.com/product/gray-membership/',
    );
  });

  it('shows a tier Square has not priced yet without a price or a way to join', async () => {
    const { tier } = await setup();
    expect(tier('blue').querySelector('.tier__price')).toBeNull();
    expect(tier('blue').querySelector('.tier__tba')?.textContent).toBe('Price to be announced');
    expect(tier('blue').querySelector('a')).toBeNull();
    expect(tier('blue').querySelector('.tier__save')?.textContent).toBe('15% off every class');
  });

  it('describes only the priced tiers as offers to search engines', async () => {
    const { jsonLd } = await setup();
    const graph = jsonLd().flatMap(
      (doc) => (doc['@graph'] as Record<string, unknown>[] | undefined) ?? [doc],
    );
    const offers = graph.filter((node) => node['@type'] === 'Offer');
    expect(offers.map((o) => [o['name'], o['price']])).toEqual([
      ['Gray membership', '100.00'],
      ['Black membership', '300.00'],
    ]);
  });
});
