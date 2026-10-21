import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RESPONSE_INIT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { MOCK_CLASSES } from '../data/mock-classes';
import { MEMBERSHIPS, MOCK_PRODUCTS } from '../data/mock-shop';
import { SiteApi } from './site-api.service';
import { SITE_API_CONFIG, SiteApiConfig } from './site-api.config';

const PIECE = {
  id: 'f1d2',
  slug: 'copper-cuff',
  title: 'Copper Cuff',
  artist: { name: 'Carrie Haase', slug: 'carrie-haase' },
  categories: ['jewelry'],
  image: null,
  priceCents: 26500,
  currency: 'USD',
  oneOfAKind: true,
  soldOut: false,
};

const PLAN = {
  id: 'p1',
  slug: 'gray-membership',
  name: 'Gray',
  perks: ['5% off every class & workshop'],
  discountPercent: 5,
  featured: false,
  priceCents: 10000,
  currency: 'USD',
  cadence: 'ANNUAL',
};

function setup(config: Partial<SiteApiConfig> = {}, onServer = true) {
  const response: ResponseInit = { status: 200 };
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SITE_API_CONFIG, useValue: { timeoutMs: 1000, mockFallback: false, ...config } },
      ...(onServer ? [{ provide: RESPONSE_INIT, useValue: response }] : []),
    ],
  });
  return { api: TestBed.inject(SiteApi), http: TestBed.inject(HttpTestingController), response };
}

describe('SiteApi', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('asks the site API relatively, so the server’s render and the browser share one transfer-cache entry', async () => {
    const { api, http } = setup();

    const page = firstValueFrom(
      api.products({
        category: 'jewelry',
        artist: 'carrie-haase',
        sort: 'price-asc',
        cursor: 'abc',
        limit: 12,
      }),
    );
    const req = http.expectOne((r) => r.url === '/api/v1/site/shop');
    expect(req.request.method).toBe('GET');
    expect(req.request.urlWithParams).toBe(
      '/api/v1/site/shop?category=jewelry&artist=carrie-haase&sort=price-asc&cursor=abc&limit=12',
    );
    req.flush({ items: [PIECE], nextCursor: 'def', totalCount: 312 });

    const result = await page;
    expect(result.total).toBe(312);
    expect(result.nextCursor).toBe('def');
    expect(result.items.map((p) => [p.id, p.name, p.price])).toEqual([
      ['f1d2', 'Copper Cuff', 26500],
    ]);
  });

  it('leaves out every filter the page did not set', async () => {
    const { api, http } = setup();
    const page = firstValueFrom(api.products());
    http.expectOne('/api/v1/site/shop').flush({ items: [], nextCursor: null, totalCount: 0 });
    expect((await page).items).toEqual([]);
  });

  it('reads each collection from its own route', async () => {
    const { api, http } = setup();
    const reads: [Promise<unknown>, string][] = [
      [firstValueFrom(api.classes()), '/api/v1/site/classes'],
      [firstValueFrom(api.instructors()), '/api/v1/site/instructors'],
      [firstValueFrom(api.exhibitions()), '/api/v1/site/exhibitions'],
      [firstValueFrom(api.artists()), '/api/v1/site/artists'],
      [firstValueFrom(api.memberships()), '/api/v1/site/memberships'],
    ];
    for (const [, url] of reads) http.expectOne(url).flush([]);
    for (const [read] of reads) expect(await read).toEqual([]);
  });

  it('maps a tier, a page and a piece', async () => {
    const { api, http } = setup();

    const tiers = firstValueFrom(api.memberships());
    http.expectOne('/api/v1/site/memberships').flush([PLAN]);
    expect((await tiers)[0]).toMatchObject({ slug: 'gray-membership', price: 10000, discount: 5 });

    const page = firstValueFrom(api.page('privacy'));
    http.expectOne('/api/v1/site/pages/privacy').flush({
      id: 'pg',
      slug: 'privacy',
      title: 'Privacy Policy',
      bodyHtml: '<p>We keep little.</p>',
      updatedAt: '2026-10-01T00:00:00+00:00',
    });
    expect(await page).toEqual({
      slug: 'privacy',
      title: 'Privacy Policy',
      html: '<p>We keep little.</p>',
    });

    const piece = firstValueFrom(api.product('copper cuff'));
    http
      .expectOne('/api/v1/site/shop/copper%20cuff')
      .flush({ ...PIECE, descriptionHtml: '<p>Hammered.</p>', images: [] });
    expect((await piece)?.description).toBe('Hammered.');
  });

  it('treats a real 404 as a missing record, and leaves the page’s status to the page', async () => {
    const { api, http, response } = setup();

    const piece = firstValueFrom(api.product('gone'));
    http
      .expectOne('/api/v1/site/shop/gone')
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });

    expect(await piece).toBeUndefined();
    expect(response.status).toBe(200);
  });

  it('makes a failed read an empty answer and the server’s response a 503 nobody caches', async () => {
    const { api, http, response } = setup();

    const classes = firstValueFrom(api.classes());
    http
      .expectOne('/api/v1/site/classes')
      .flush('upstream down', { status: 502, statusText: 'Bad Gateway' });

    expect(await classes).toEqual([]);
    expect(response.status).toBe(503);
    expect(new Headers(response.headers).get('Cache-Control')).toBe('no-store');
  });

  it('treats a body it cannot decode as a failed read', async () => {
    const { api, http, response } = setup();

    const page = firstValueFrom(api.products());
    http
      .expectOne('/api/v1/site/shop')
      .flush({ items: [{ id: 7 }], nextCursor: null, totalCount: 1 });

    expect(await page).toEqual({ items: [], total: 0, nextCursor: null });
    expect(response.status).toBe(503);
  });

  it('refuses a shop page without a whole-number total', async () => {
    const { api, http } = setup();
    for (const body of [
      { items: [], nextCursor: null },
      { items: [], nextCursor: null, totalCount: 1.5 },
    ]) {
      const page = firstValueFrom(api.products());
      http.expectOne('/api/v1/site/shop').flush(body);
      expect(await page).toEqual({ items: [], total: 0, nextCursor: null });
    }
  });

  it('gives up on a read that takes longer than the timeout', async () => {
    vi.useFakeTimers();
    try {
      const { api, http, response } = setup({ timeoutMs: 50 });
      const tiers = firstValueFrom(api.memberships());
      const req = http.expectOne('/api/v1/site/memberships');
      vi.advanceTimersByTime(60);

      expect(await tiers).toEqual([]);
      expect(req.cancelled).toBe(true);
      expect(response.status).toBe(503);
    } finally {
      vi.useRealTimers();
    }
  });

  it('serves the bundled sample data instead, with the flag on, and leaves the response alone', async () => {
    const { api, http, response } = setup({ mockFallback: true });

    const classes = firstValueFrom(api.classes());
    const tiers = firstValueFrom(api.memberships());
    const shop = firstValueFrom(api.products({ category: 'jewelry', limit: 2 }));
    const piece = firstValueFrom(api.product(MOCK_PRODUCTS[0].slug));
    for (const url of [
      '/api/v1/site/classes',
      '/api/v1/site/memberships',
      '/api/v1/site/shop',
      `/api/v1/site/shop/${MOCK_PRODUCTS[0].slug}`,
    ]) {
      http.expectOne((r) => r.url === url).flush(null, { status: 500, statusText: 'Server Error' });
    }

    expect((await classes).length).toBe(MOCK_CLASSES.length);
    expect(await tiers).toEqual(MEMBERSHIPS);
    const jewelry = MOCK_PRODUCTS.filter((p) => p.categories.includes('jewelry'));
    expect(await shop).toEqual({
      items: jewelry.slice(0, 2),
      total: jewelry.length,
      nextCursor: jewelry.length > 2 ? '2' : null,
    });
    expect(await piece).toEqual(MOCK_PRODUCTS[0]);
    expect(response.status).toBe(200);
  });

  it('has no response to mark in the browser', async () => {
    const { api, http } = setup({}, false);
    const classes = firstValueFrom(api.classes());
    http.expectOne('/api/v1/site/classes').flush(null, { status: 500, statusText: 'Server Error' });
    expect(await classes).toEqual([]);
  });
});
