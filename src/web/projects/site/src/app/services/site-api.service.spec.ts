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

const ARTIST = {
  id: '7f1c',
  slug: 'carrie-haase',
  name: 'Carrie Haase',
  medium: 'Jewelry',
  active: true,
  portrait: null,
  bioText: 'Metalsmith in Edgewater.',
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

  it('reads one artist with their CMS profile and the first page of their work', async () => {
    const { api, http } = setup();

    const page = firstValueFrom(api.artist('carrie haase'));
    http.expectOne('/api/v1/site/artists/carrie%20haase').flush({
      artist: {
        id: 'a1',
        slug: 'carrie-haase',
        name: 'Carrie Haase',
        medium: 'Jewelry',
        active: true,
        portrait: null,
        bioText: 'Metalsmith.\n\nEdgewater.',
      },
      works: { items: [PIECE], nextCursor: 'def' },
    });

    const result = await page;
    expect(result?.artist).toMatchObject({
      name: 'Carrie Haase',
      bio: ['Metalsmith.', 'Edgewater.'],
      active: true,
    });
    expect(result?.works.map((p) => p.slug)).toEqual(['copper-cuff']);
    expect(result?.nextCursor).toBe('def');
  });

  it('makes a missing artist undefined, and a failed read a work-less page named from the slug', async () => {
    const { api, http, response } = setup();

    const gone = firstValueFrom(api.artist('gone'));
    http
      .expectOne('/api/v1/site/artists/gone')
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });
    expect(await gone).toBeUndefined();
    expect(response.status).toBe(200);

    const down = firstValueFrom(api.artist('jane-doe'));
    http
      .expectOne('/api/v1/site/artists/jane-doe')
      .flush({ artist: { id: 'a1' }, works: { items: [], nextCursor: null } });
    expect(await down).toEqual({
      artist: { slug: 'jane-doe', name: 'Jane Doe' },
      works: [],
      nextCursor: null,
    });
    expect(response.status).toBe(503);
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

  it('reads a preview by its token, and keeps the token in the path it was given', async () => {
    const { api, http, response } = setup();

    const preview = firstValueFrom(api.preview('tok en/+'));
    const req = http.expectOne((r) => r.url.startsWith('/api/v1/site/preview/'));
    expect(req.request.url).toBe('/api/v1/site/preview/tok%20en%2F%2B');
    req.flush({
      entityType: 'artist',
      entityId: '7f1c',
      path: '/artists/carrie-haase',
      status: 'Draft',
      noIndex: true,
      artist: ARTIST,
    });

    expect((await preview)?.artist?.name).toBe('Carrie Haase');
    // A preview is a staff tool: a read of one never marks the render unavailable.
    expect(response.status).toBe(200);
  });

  it('makes every unusable preview token one answer, and never the sample data', async () => {
    const { api, http, response } = setup({ mockFallback: true });

    const expired = firstValueFrom(api.preview('expired'));
    http
      .expectOne('/api/v1/site/preview/expired')
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });
    const broken = firstValueFrom(api.preview('broken'));
    http.expectOne('/api/v1/site/preview/broken').flush({ entityType: 'artist', noIndex: true });
    const unreachable = firstValueFrom(api.preview('unreachable'));
    http
      .expectOne('/api/v1/site/preview/unreachable')
      .flush('upstream down', { status: 502, statusText: 'Bad Gateway' });

    expect(await expired).toBeUndefined();
    expect(await broken).toBeUndefined();
    expect(await unreachable).toBeUndefined();
    expect(response.status).toBe(200);
  });

  it('refuses a preview whose type and record disagree', async () => {
    const { api, http } = setup();

    const preview = firstValueFrom(api.preview('mismatched'));
    http.expectOne('/api/v1/site/preview/mismatched').flush({
      entityType: 'exhibition',
      entityId: '7f1c',
      path: '/exhibitions/winter',
      status: 'Draft',
      noIndex: true,
      artist: ARTIST,
    });

    expect(await preview).toBeUndefined();
  });

  it('reads one show for its own page, with its write-up', async () => {
    const { api, http } = setup();

    const show = firstValueFrom(api.exhibition('winter-light'));
    http.expectOne('/api/v1/site/exhibitions/winter-light').flush({
      id: '2b4e',
      slug: 'winter-light',
      title: 'Winter Light',
      summary: 'Nine painters.',
      bodyText: 'Oils, mostly.\n\nTwo rooms of snow.',
      startDate: '2099-01-08',
      endDate: '2099-03-01',
      reception: null,
      displayTimeZone: 'America/Chicago',
      artists: [{ name: 'Zed Draft', slug: null }],
      image: null,
    });

    const page = await show;
    expect(page?.title).toBe('Winter Light');
    expect(page?.body).toEqual(['Oils, mostly.', 'Two rooms of snow.']);
    expect(page?.credits).toEqual([{ name: 'Zed Draft' }]);
  });

  it('tells a failed show read apart from a missing one, and makes the render a 503', async () => {
    const { api, http, response } = setup();

    const show = firstValueFrom(api.exhibition('winter-light'));
    http
      .expectOne('/api/v1/site/exhibitions/winter-light')
      .flush('upstream down', { status: 502, statusText: 'Bad Gateway' });

    // `null`, not `undefined`: a blip must not tell a crawler that a published show is gone.
    expect(await show).toBeNull();
    expect(response.status).toBe(503);
    expect(new Headers(response.headers).get('Cache-Control')).toBe('no-store');
  });

  it('has no show to give for a slug the API does not know', async () => {
    const { api, http, response } = setup();

    const show = firstValueFrom(api.exhibition('gone'));
    http
      .expectOne('/api/v1/site/exhibitions/gone')
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });

    expect(await show).toBeUndefined();
    expect(response.status).toBe(200);
  });

  it('reads one class and its dates, and keeps a class with none', async () => {
    const { api, http } = setup();

    const page = firstValueFrom(api.classPage('screen-printing'));
    http.expectOne('/api/v1/site/classes/screen-printing').flush({
      id: 'c1',
      slug: 'screen-printing',
      title: 'Screen Printing Night',
      summary: 'Pull a print.',
      bodyHtml: '<p>Bring an apron.</p>',
      classType: 'Printmaking',
      level: 'Beginner',
      image: null,
      fromPriceCents: null,
      currency: 'USD',
      // Every run has ended, which is what the API sends for a class between terms.
      runs: [],
    });

    const klass = await page;
    expect(klass?.title).toBe('Screen Printing Night');
    expect(klass?.body).toBe('<p>Bring an apron.</p>');
    expect(klass?.runs).toEqual([]);
  });

  it('tells a failed class read apart from a missing one', async () => {
    const { api, http, response } = setup();

    const missing = firstValueFrom(api.classPage('gone'));
    http
      .expectOne('/api/v1/site/classes/gone')
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });
    expect(await missing).toBeUndefined();
    expect(response.status).toBe(200);

    const failed = firstValueFrom(api.classPage('screen-printing'));
    http
      .expectOne('/api/v1/site/classes/screen-printing')
      .flush('upstream down', { status: 502, statusText: 'Bad Gateway' });
    expect(await failed).toBeNull();
    expect(response.status).toBe(503);
  });
});
