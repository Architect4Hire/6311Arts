import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { PreviewContext } from '../core/preview';
import { SitePreview } from '../models/site';
import { PreviewSiteApi } from './preview-site-api.service';
import { SITE_API_CONFIG } from './site-api.config';

const ITEM = {
  id: 'f1d2',
  slug: 'copper-cuff',
  title: 'Copper Cuff',
  artist: { name: 'Carrie Haase', slug: 'carrie-haase' },
  categories: ['jewelry'],
  image: null,
  images: [],
  descriptionHtml: '<p>Hammered copper.</p>',
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

const EXHIBITION = {
  id: '2b4e',
  slug: 'winter-light',
  title: 'Winter Light',
  summary: 'Nine painters on the shortest days.',
  bodyText: 'Nine painters.',
  startDate: '2099-01-08',
  endDate: '2099-03-01',
  reception: null,
  displayTimeZone: 'America/Chicago',
  artists: [],
  image: null,
};

/** A preview of one record, with the six members it does not set left null. */
function previewOf(body: Partial<SitePreview>): SitePreview {
  return {
    entityType: 'shop-item',
    entityId: 'f1d2',
    path: '/shop/copper-cuff',
    status: 'Draft',
    noIndex: true,
    artist: null,
    instructor: null,
    exhibition: null,
    page: null,
    classOffering: null,
    shopItem: null,
    membershipPlan: null,
    ...body,
  };
}

function setup(preview: SitePreview) {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SITE_API_CONFIG, useValue: { timeoutMs: 1000, mockFallback: false } },
      PreviewSiteApi,
    ],
  });
  TestBed.inject(PreviewContext).enter(preview);
  return { api: TestBed.inject(PreviewSiteApi), http: TestBed.inject(HttpTestingController) };
}

describe('PreviewSiteApi', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.inject(PreviewContext).leave();
  });

  it('answers the read for the draft itself without asking the API, which has nothing to tell it', async () => {
    const { api } = setup(previewOf({ shopItem: ITEM }));

    const piece = await firstValueFrom(api.product('copper-cuff'));

    expect(piece?.name).toBe('Copper Cuff');
    expect(piece?.price).toBe(26500);
  });

  it('never answers for an address that is not the draft’s own', async () => {
    const { api, http } = setup(previewOf({ shopItem: ITEM }));

    const other = firstValueFrom(api.product('someone-elses-ring'));
    http
      .expectOne('/api/v1/site/shop/someone-elses-ring')
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });

    expect(await other).toBeUndefined();
  });

  it('leaves every read a preview of another type cannot answer to the real API', async () => {
    const { api, http } = setup(previewOf({ entityType: 'membership-plan', membershipPlan: PLAN }));

    const piece = firstValueFrom(api.product('copper-cuff'));
    http.expectOne('/api/v1/site/shop/copper-cuff').flush(ITEM);
    const page = firstValueFrom(api.page('privacy'));
    http.expectOne('/api/v1/site/pages/privacy').flush({
      id: '9c0a',
      slug: 'privacy',
      title: 'Privacy Policy',
      bodyHtml: '<p>What we keep.</p>',
      updatedAt: '2026-10-30T12:00:00+00:00',
    });

    expect((await piece)?.name).toBe('Copper Cuff');
    expect((await page)?.title).toBe('Privacy Policy');
  });

  it('shows a draft tier among the published ones, because that is what makes the page read true', async () => {
    const { api, http } = setup(previewOf({ entityType: 'membership-plan', membershipPlan: PLAN }));

    const tiers = firstValueFrom(api.memberships());
    http
      .expectOne('/api/v1/site/memberships')
      .flush([{ ...PLAN, id: 'p2', slug: 'black', name: 'Black' }]);

    // The published tier keeps its place, and the draft joins it: the API's order carries no room for a new one.
    expect((await tiers).map((t) => t.name)).toEqual(['Black', 'Gray']);
  });

  it('replaces a tier’s published self when the draft is an edit to it', async () => {
    const { api, http } = setup(
      previewOf({
        entityType: 'membership-plan',
        membershipPlan: { ...PLAN, name: 'Gray, renamed' },
      }),
    );

    const tiers = firstValueFrom(api.memberships());
    http
      .expectOne('/api/v1/site/memberships')
      .flush([PLAN, { ...PLAN, id: 'p2', slug: 'black', name: 'Black' }]);

    expect((await tiers).map((t) => t.name)).toEqual(['Gray, renamed', 'Black']);
  });

  it('answers the show’s own page for the draft, and the API for any other show', async () => {
    const { api, http } = setup(previewOf({ entityType: 'exhibition', exhibition: EXHIBITION }));

    const show = await firstValueFrom(api.exhibition('winter-light'));
    const other = firstValueFrom(api.exhibition('autumn-salon'));
    http
      .expectOne('/api/v1/site/exhibitions/autumn-salon')
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });

    expect(show?.title).toBe('Winter Light');
    expect(show?.body).toEqual(['Nine painters.']);
    expect(await other).toBeUndefined();
  });

  it('answers the class’s own page for the draft, dates or no dates', async () => {
    const { api, http } = setup(
      previewOf({
        entityType: 'class-offering',
        classOffering: {
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
          runs: [],
        },
      }),
    );

    const klass = await firstValueFrom(api.classPage('screen-printing'));
    const other = firstValueFrom(api.classPage('life-drawing'));
    http
      .expectOne('/api/v1/site/classes/life-drawing')
      .flush({ code: 'resource.not_found' }, { status: 404, statusText: 'Not Found' });

    expect(klass?.title).toBe('Screen Printing Night');
    expect(klass?.runs).toEqual([]);
    expect(await other).toBeUndefined();
  });
});
