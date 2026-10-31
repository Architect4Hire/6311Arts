import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RESPONSE_INIT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { PreviewContext } from '../../core/preview';
import { ClassDetail } from '../class-detail/class-detail';
import { ExhibitionDetail } from '../exhibition-detail/exhibition-detail';
import { SITE_API_CONFIG } from '../../services/site-api.config';
import { Preview } from './preview';

const TOKEN = 'CfDJ8EmqFYZl5SFH';

const ARTIST = {
  id: '7f1c',
  slug: 'carrie-haase',
  name: 'Carrie Haase',
  medium: 'Jewelry',
  active: true,
  portrait: null,
  bioText: 'Metalsmith in Edgewater.',
};

/** Dated well ahead, so the show is in the spotlight whenever the suite runs. */
const EXHIBITION = {
  id: '2b4e',
  slug: 'winter-light',
  title: 'Winter Light',
  summary: 'Nine painters on the shortest days of the year.',
  bodyText: 'Oils, mostly, and two rooms of snow.',
  startDate: '2099-01-08',
  endDate: '2099-03-01',
  reception: null,
  displayTimeZone: 'America/Chicago',
  artists: [{ name: 'Carrie Haase', slug: null }],
  image: null,
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

/** One run still to come, which is what makes a class renderable at all. */
const RUN = {
  id: 'run-1',
  label: 'Autumn',
  instructor: { name: 'Jane Doe', slug: 'jane-doe' },
  cadence: 'Saturdays',
  displayTimeZone: 'America/Chicago',
  startsAt: '2099-11-10T18:00:00+00:00',
  endsAt: '2099-11-10T21:00:00+00:00',
  sessionCount: 1,
  capacity: 12,
  priceCents: 4500,
  currency: 'USD',
  seatsRemaining: 3,
  soldOut: false,
  sessions: [
    { id: 's1', startsAt: '2099-11-10T18:00:00+00:00', endsAt: '2099-11-10T21:00:00+00:00' },
  ],
};

function classOffering(runs: unknown[]) {
  return {
    id: 'c1',
    slug: 'screen-printing',
    title: 'Screen Printing Night',
    summary: 'Pull a print.',
    bodyHtml: '<p>Bring an apron.</p>',
    classType: 'Printmaking',
    level: 'Beginner',
    image: null,
    fromPriceCents: 4500,
    currency: 'USD',
    runs,
  };
}

async function render() {
  // The hero's slideshow asks whether motion is reduced; jsdom has no matchMedia.
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  const response: ResponseInit = { status: 200 };
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SITE_API_CONFIG, useValue: { timeoutMs: 1000, mockFallback: false } },
      { provide: RESPONSE_INIT, useValue: response },
      provideRouter([{ path: 'preview/:token', component: Preview }], withComponentInputBinding()),
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(`/preview/${TOKEN}`);
  return {
    http,
    response,
    harness,
    /** Answers the preview read with this body, and renders whatever the page made of it. */
    answer(body: Record<string, unknown>, status = 200) {
      http.expectOne(`/api/v1/site/preview/${TOKEN}`).flush(body, {
        status,
        statusText: status === 200 ? 'OK' : 'Not Found',
      });
      harness.detectChanges();
      return harness.routeNativeElement!;
    },
    head: () => TestBed.inject(DOCUMENT).head,
    context: () => TestBed.inject(PreviewContext),
  };
}

/**
 * The exhibition page at its own address, with the same record a preview would carry, so the two renders can be
 * compared. It goes through the real `SiteApi`, as the preview's seeded one goes through the same mappers.
 */
async function renderTheRouteItself() {
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SITE_API_CONFIG, useValue: { timeoutMs: 1000, mockFallback: false } },
      provideRouter(
        [{ path: 'exhibitions/:slug', component: ExhibitionDetail }],
        withComponentInputBinding(),
      ),
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl('/exhibitions/winter-light');
  http.expectOne('/api/v1/site/exhibitions/winter-light').flush(EXHIBITION);
  http.expectOne('/api/v1/site/exhibitions').flush([]);
  harness.detectChanges();
  return harness.routeNativeElement!.innerHTML;
}

/** The class page at its own address, with the same record a preview would carry. */
async function renderTheClassRouteItself() {
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SITE_API_CONFIG, useValue: { timeoutMs: 1000, mockFallback: false } },
      provideRouter(
        [{ path: 'classes/:slug', component: ClassDetail }],
        withComponentInputBinding(),
      ),
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl('/classes/screen-printing');
  http.expectOne('/api/v1/site/classes/screen-printing').flush(classOffering([RUN]));
  http.expectOne('/api/v1/site/classes').flush([]);
  http.expectOne('/api/v1/site/memberships').flush([]);
  harness.detectChanges();
  return harness.routeNativeElement!.innerHTML;
}

function preview(body: Record<string, unknown>) {
  return { entityId: '7f1c', status: 'Draft', noIndex: true, ...body };
}

const text = (element: Element, selector: string) =>
  [...element.querySelectorAll(selector)].map((e) => e.textContent!.replace(/\s+/g, ' ').trim());

describe('Preview', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.inject(HttpTestingController).verify();
  });

  it('renders the show’s own page for a draft exhibition, write-up and all', async () => {
    const { answer, http, harness } = await render();

    const element = answer(
      preview({
        entityType: 'exhibition',
        path: '/exhibitions/winter-light',
        exhibition: EXHIBITION,
      }),
    );
    // "More exhibitions" is the published list, as it is on the live page.
    http.expectOne('/api/v1/site/exhibitions').flush([]);
    harness.detectChanges();

    expect(text(element, '.preview-bar__what')).toEqual([
      'Preview Draft exhibition, as the site will show it.',
    ]);
    expect(text(element, '.preview-bar__where')[0]).toContain('/exhibitions/winter-light');
    expect(text(element, '.detail__title')).toEqual(['Winter Light']);
    expect(text(element, '.detail__lead')).toEqual([
      'Nine painters on the shortest days of the year.',
      'Oils, mostly, and two rooms of snow.',
    ]);
    expect(element.querySelector('app-not-found')).toBeNull();
  });

  it('keeps a preview out of every index: noindex, nofollow, no canonical and no-store', async () => {
    const { answer, response, head, http } = await render();

    answer(preview({ entityType: 'membership-plan', path: '/memberships', membershipPlan: PLAN }));
    http.expectOne('/api/v1/site/memberships').flush([]);

    expect(head().querySelector('meta[name="robots"]')?.getAttribute('content')).toBe(
      'noindex, nofollow',
    );
    // The record is not published, so the address it will have is not a canonical yet.
    expect(head().querySelector('link[rel="canonical"]')).toBeNull();
    const headers = new Headers(response.headers);
    expect(headers.get('Cache-Control')).toBe('no-store');
    expect(headers.get('X-Robots-Tag')).toBe('noindex, nofollow');
  });

  it('hands a detail page the draft it could not read for itself, and the live site around it', async () => {
    const { answer, http, harness } = await render();

    const element = answer(
      preview({ entityType: 'artist', path: '/artists/carrie-haase', artist: ARTIST }),
    );
    // The surroundings stay the real site's: the artist's work comes from the published shop.
    http
      .expectOne((r) => r.url === '/api/v1/site/shop')
      .flush({
        items: [],
        nextCursor: null,
        totalCount: 0,
      });
    http.expectOne('/api/v1/site/instructors').flush([]);
    harness.detectChanges();

    expect(text(element, '.detail__title')).toEqual(['Carrie Haase']);
    expect(text(element, '.detail__lead')).toEqual(['Metalsmith in Edgewater.']);
  });

  it('is one answer for every token that cannot be used: the site’s 404, and nothing left behind', async () => {
    const { answer, response, context } = await render();

    const element = answer({ code: 'resource.not_found' }, 404);

    expect(element.querySelector('app-not-found')).not.toBeNull();
    expect(text(element, '.preview-bar__what')).toEqual([]);
    expect(response.status).toBe(404);
    expect(context().active()).toBe(false);
    // The headers are the page's, not the answer's: a refusal is no more cacheable or indexable than a draft.
    const headers = new Headers(response.headers);
    expect(headers.get('Cache-Control')).toBe('no-store');
    expect(headers.get('X-Robots-Tag')).toBe('noindex, nofollow');
  });

  it('keeps the same headers when the preview read fails outright', async () => {
    const { answer, response } = await render();

    answer({ code: 'server.error' }, 502);

    const headers = new Headers(response.headers);
    expect(headers.get('Cache-Control')).toBe('no-store');
    expect(headers.get('X-Robots-Tag')).toBe('noindex, nofollow');
  });

  it('starts over when a second link is opened in the same tab', async () => {
    const { answer, http, harness, context } = await render();

    answer(preview({ entityType: 'artist', path: '/artists/carrie-haase', artist: ARTIST }));
    http
      .expectOne((r) => r.url === '/api/v1/site/shop')
      .flush({
        items: [],
        nextCursor: null,
        totalCount: 0,
      });
    http.expectOne('/api/v1/site/instructors').flush([]);
    harness.detectChanges();

    await harness.navigateByUrl('/preview/second-token');

    // Nothing of the first draft is on screen under the second address while its token is being read.
    expect(context().active()).toBe(false);
    expect(harness.routeNativeElement!.querySelector('.preview-bar')).toBeNull();

    http.expectOne('/api/v1/site/preview/second-token').flush(
      preview({
        entityType: 'page',
        path: '/privacy',
        page: {
          id: '9c0a',
          slug: 'privacy',
          title: 'Privacy Policy',
          bodyHtml: '<p>What we keep.</p>',
          updatedAt: '2026-10-30T12:00:00+00:00',
        },
      }),
    );
    harness.detectChanges();

    const element = harness.routeNativeElement!;
    expect(text(element, '.preview-bar__what')).toEqual([
      'Preview Draft page, as the site will show it.',
    ]);
    expect(text(element, '.prose')).toEqual(['What we keep.']);
  });

  it('renders the class page for a draft whose run is still to come', async () => {
    const { answer, http, harness } = await render();

    const element = answer(
      preview({
        entityType: 'class-offering',
        path: '/classes/screen-printing',
        classOffering: classOffering([RUN]),
      }),
    );
    // The calendar around it stays the published one, and the tiers are the real discount.
    http.expectOne('/api/v1/site/classes').flush([]);
    http.expectOne('/api/v1/site/memberships').flush([]);
    harness.detectChanges();

    expect(text(element, '.preview-bar__what')).toEqual([
      'Preview Draft class, as the site will show it.',
    ]);
    expect(element.querySelector('app-not-found')).toBeNull();
    expect(text(element, '.detail__title')).toEqual(['Screen Printing Night']);
  });

  it('renders the class page for a draft with no dates yet, not a 404', async () => {
    const { answer, http, harness } = await render();

    const element = answer(
      preview({
        entityType: 'class-offering',
        path: '/classes/screen-printing',
        classOffering: classOffering([]),
      }),
    );
    http.expectOne('/api/v1/site/classes').flush([]);
    http.expectOne('/api/v1/site/memberships').flush([]);
    harness.detectChanges();

    // The copy is what staff came to see; the page says the calendar is empty (ADR 0065).
    expect(element.querySelector('app-not-found')).toBeNull();
    expect(text(element, '.detail__title')).toEqual(['Screen Printing Night']);
    expect(text(element, '.facts li').join(' ')).toContain('No dates on the calendar just now');
  });

  it('takes no room in the page it is previewing: the flag floats and the host is not a box', async () => {
    const { answer, http, harness } = await render();

    const element = answer(
      preview({
        entityType: 'exhibition',
        path: '/exhibitions/winter-light',
        exhibition: EXHIBITION,
      }),
    );
    http.expectOne('/api/v1/site/exhibitions').flush([]);
    harness.detectChanges();

    // The nav is fixed and the hero pays for it with its own top padding, so anything in flow above the page
    // would sit under the nav and push every previewed page down (ADR 0063).
    expect(getComputedStyle(harness.routeNativeElement!).display).toBe('contents');
    expect(getComputedStyle(element.querySelector('.preview-bar')!).position).toBe('fixed');
    // The page's own first section is what follows, exactly as it does on the site.
    expect(element.querySelector('app-exhibition-detail')).not.toBeNull();
  });

  it('folds the flag away to its tag, for the part of the page it covers', async () => {
    const { answer, http, harness } = await render();

    const element = answer(
      preview({
        entityType: 'exhibition',
        path: '/exhibitions/winter-light',
        exhibition: EXHIBITION,
      }),
    );
    http.expectOne('/api/v1/site/exhibitions').flush([]);
    harness.detectChanges();

    element.querySelector<HTMLButtonElement>('.preview-bar__fold')!.click();
    harness.detectChanges();

    expect(text(element, '.preview-bar__what')).toEqual(['Preview Draft exhibition']);
    expect(element.querySelector('.preview-bar__where')).toBeNull();
    expect(text(element, '.preview-bar__fold')).toEqual(['Show preview details']);
  });

  it('renders the page exactly as its own route does, markup for markup', async () => {
    // What a visitor will get at /exhibitions/winter-light.
    const live = await renderTheRouteItself();
    // Guard against both sides being empty, which would make the comparison below say nothing.
    expect(live).toContain('Winter Light');

    TestBed.resetTestingModule();

    // The same record, through a preview link.
    const { answer, http, harness } = await render();
    const element = answer(
      preview({
        entityType: 'exhibition',
        path: '/exhibitions/winter-light',
        exhibition: EXHIBITION,
      }),
    );
    http.expectOne('/api/v1/site/exhibitions').flush([]);
    harness.detectChanges();

    // Not "looks similar": the page's own markup is identical, so its layout cannot differ. The flag is outside
    // this element, floating over it (ADR 0063).
    expect(element.querySelector('app-exhibition-detail')!.innerHTML).toBe(live);
  });

  it('renders a class exactly as its own route does, markup for markup', async () => {
    // The type the client checked first: a class previewed must be the class page, not an approximation.
    const live = await renderTheClassRouteItself();
    expect(live).toContain('Screen Printing Night');

    TestBed.resetTestingModule();

    const { answer, http, harness } = await render();
    const element = answer(
      preview({
        entityType: 'class-offering',
        path: '/classes/screen-printing',
        classOffering: classOffering([RUN]),
      }),
    );
    http.expectOne('/api/v1/site/classes').flush([]);
    http.expectOne('/api/v1/site/memberships').flush([]);
    harness.detectChanges();

    expect(element.querySelector('app-class-detail')!.innerHTML).toBe(live);
  });
});
