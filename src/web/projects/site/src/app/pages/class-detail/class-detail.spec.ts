import { provideHttpClient } from '@angular/common/http';
import { RESPONSE_INIT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { NEVER, Observable, of } from 'rxjs';
import { ArtClass, ClassPage, MembershipTier } from '../../core/models';
import { SiteApi } from '../../services/site-api.service';
import { ClassDetail } from './class-detail';

const PRINTING: ArtClass = {
  id: 'run-1',
  slug: 'screen-printing',
  title: 'Screen Printing Night',
  start: '2026-11-10T18:00:00-06:00',
  end: '2026-11-10T21:00:00-06:00',
  sessions: 1,
  type: 'Printmaking',
  level: 'Beginner',
  price: 4500,
  instructor: 'Jane Doe',
  summary: 'Pull a print.',
  spotsLeft: 3,
  capacity: 12,
};

/** In the order staff set, which is not the order of price or discount. */
const TIERS: MembershipTier[] = [
  { id: 't2', slug: 'black-membership', name: 'Black', price: 30000, discount: 10, perks: [] },
  { id: 't1', slug: 'gray-membership', name: 'Gray', price: 10000, discount: 5, perks: [] },
  { id: 't3', slug: 'blue-membership', name: 'Blue', price: undefined, discount: 15, perks: [] },
];

/** The class as its page reads it (ADR 0065): the copy, and whatever is still on the calendar. */
function classPage(runs: ArtClass[], body?: string): ClassPage {
  return {
    slug: 'screen-printing',
    title: 'Screen Printing Night',
    summary: 'Pull a print.',
    body,
    type: 'Printmaking',
    level: 'Beginner',
    runs,
  };
}

async function setup(
  page: ClassPage | undefined | null | Observable<ClassPage | undefined | null>,
  tiers = TIERS,
  url = '/classes/screen-printing',
) {
  const response: ResponseInit = {};
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter(
        [{ path: 'classes/:slug', component: ClassDetail }],
        withComponentInputBinding(),
      ),
      { provide: RESPONSE_INIT, useValue: response },
      {
        provide: SiteApi,
        useValue: {
          classPage: (slug: string) =>
            page && 'subscribe' in page ? page : of(page && page.slug === slug ? page : undefined),
          classes: () => of([]),
          memberships: () => of(tiers),
        },
      },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  const element = harness.routeNativeElement!;
  const text = (selector: string) =>
    element.querySelector(selector)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const link = (label: string) =>
    [...element.querySelectorAll<HTMLAnchorElement>('a')].find(
      (a) => a.textContent?.trim() === label,
    );
  return { element, text, link, response };
}

describe('ClassDetail', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('reserves through WooCommerce’s page for the class until Phase 5', async () => {
    const { link, text } = await setup(classPage([PRINTING]));
    expect(text('.buy__price')).toBe('$45');
    expect(link('Reserve your spot')?.getAttribute('href')).toBe(
      'https://6311arts.com/product/screen-printing/',
    );
  });

  it('quotes members the smallest discount and the cheapest tier Square has priced', async () => {
    const { text } = await setup(classPage([PRINTING]));
    // $45 less 5% is $42.75; the Gray tier at $100 is the cheapest with a price.
    expect(text('.buy__note')).toBe('Members pay $42.75 or less. Join from $100/yr.');
  });

  it('leaves the member note out while no tier is published', async () => {
    const { element } = await setup(classPage([PRINTING]), []);
    expect(element.querySelector('.buy__note')).toBeNull();
  });

  it('offers nothing to reserve until Square has priced the run', async () => {
    const { link, element } = await setup(classPage([{ ...PRINTING, price: undefined }]));
    expect(link('Reserve your spot')).toBeUndefined();
    expect(element.querySelector('.buy__price')).toBeNull();
    // The member note is gone; the only note is the one standing in for the price.
    expect([...element.querySelectorAll('.buy__note')].map((n) => n.textContent?.trim())).toEqual([
      'Price to be announced',
    ]);
  });

  it('sends a sold-out run’s visitors to another date', async () => {
    const { link } = await setup(classPage([{ ...PRINTING, spotsLeft: 0 }]));
    expect(link('Reserve your spot')).toBeUndefined();
    expect(link('Find another date')?.getAttribute('href')).toBe('/classes');
  });

  it('shows the not-found page for a class that is not on the calendar', async () => {
    const { text, response } = await setup(classPage([PRINTING]), TIERS, '/classes/nothing-here');
    expect(text('h1')).toContain('Nothing on this wall');
    expect(response.status).toBe(404);
  });

  it('answers 404 once an unpublished class has left the site read', async () => {
    // Unpublishing takes the class out of /api/v1/site/classes/{slug}, which answers 404 (ADR 0065).
    const { text, response } = await setup(undefined);
    expect(text('h1')).toContain('Nothing on this wall');
    expect(response.status).toBe(404);
  });

  it('keeps a published class between terms, instead of answering 404 on its own address', async () => {
    // Its runs have all ended, so the calendar no longer lists it — but the class is published and the sitemap
    // still names it, so the page is the copy with no dates (ADR 0065).
    const { element, text, response } = await setup(classPage([]));

    expect(text('.detail__title')).toBe('Screen Printing Night');
    expect(element.querySelector('app-not-found')).toBeNull();
    expect(response.status).toBeUndefined();
    expect(text('.facts li')).toContain('No dates on the calendar just now');
    expect(text('.buy__note')).toContain('not on the calendar at the moment');
  });

  it('renders the "More details" staff wrote, which the page never used to show', async () => {
    const { element } = await setup(classPage([PRINTING], '<p>Bring an apron.</p>'));

    expect(element.querySelector('.prose')?.innerHTML).toBe('<p>Bring an apron.</p>');
  });

  it('lists the class’s other dates, which the page used to hide', async () => {
    const second = { ...PRINTING, id: 'run-2', start: '2026-12-08T18:00:00-06:00' };
    const { element } = await setup(classPage([PRINTING, second]));

    const others = element.querySelectorAll('.class-list app-class-card');
    expect(others.length).toBe(1);
  });

  it('keeps a published class a 200, and says nothing is missing while the list is still loading', async () => {
    expect((await setup(classPage([PRINTING]))).response.status).toBeUndefined();
    TestBed.resetTestingModule();

    const loading = await setup(NEVER);
    expect(loading.response.status).toBeUndefined();
    expect(loading.text('h1')).not.toContain('Nothing on this wall');
  });
});
