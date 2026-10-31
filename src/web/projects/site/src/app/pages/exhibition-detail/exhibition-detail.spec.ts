import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { Exhibition } from '../../core/models';
import { SiteApi } from '../../services/site-api.service';
import { ExhibitionDetail } from './exhibition-detail';

const WINTER: Exhibition = {
  slug: 'winter-light',
  title: 'Winter Light',
  start: '2099-01-08',
  end: '2099-03-01',
  summary: 'Nine painters on the shortest days of the year.',
  artists: ['Carrie Haase', 'Zed Draft'],
  credits: [{ name: 'Carrie Haase', slug: 'carrie-haase' }, { name: 'Zed Draft' }],
  body: ['Oils, mostly.', 'Two rooms of snow.'],
  image: '/media/abc/w1600.jpg',
  reception: {
    start: '2099-01-08T18:00:00-06:00',
    end: '2099-01-08T21:00:00-06:00',
    note: 'Cash bar',
  },
};

const AUTUMN: Exhibition = {
  slug: 'autumn-salon',
  title: 'Autumn Salon',
  start: '2098-09-01',
  end: '2098-10-30',
  summary: 'The members’ show.',
  artists: [],
};

async function render(show: Exhibition | undefined | null, list: Exhibition[] = [WINTER, AUTUMN]) {
  // The hero's slideshow asks whether motion is reduced; jsdom has no matchMedia.
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter(
        [{ path: 'exhibitions/:slug', component: ExhibitionDetail }],
        withComponentInputBinding(),
      ),
      {
        provide: SiteApi,
        useValue: { exhibition: () => of(show), exhibitions: () => of(list) },
      },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl('/exhibitions/winter-light');
  const element = harness.routeNativeElement!;
  const text = (selector: string) =>
    [...element.querySelectorAll(selector)].map((e) => e.textContent!.replace(/\s+/g, ' ').trim());
  return { element, text };
}

describe('ExhibitionDetail', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the show: its dates, its write-up, its reception and who is in it', async () => {
    const { element, text } = await render(WINTER);

    expect(text('.detail__title')).toEqual(['Winter Light']);
    expect(text('.detail__lead')).toEqual([
      'Nine painters on the shortest days of the year.',
      'Oils, mostly.',
      'Two rooms of snow.',
    ]);
    const facts = text('.facts li');
    expect(facts.some((f) => f.startsWith('Coming up'))).toBe(true);
    expect(facts.some((f) => f.includes('Opening reception') && f.includes('Cash bar'))).toBe(true);
    expect(element.querySelector('app-art-image')).not.toBeNull();
  });

  it('links a credited artist who has a page, and names one who has not', async () => {
    const { element, text } = await render(WINTER);

    const links = [...element.querySelectorAll<HTMLAnchorElement>('.facts a')].map((a) => [
      a.textContent!.trim(),
      a.getAttribute('href'),
    ]);
    expect(links).toContainEqual(['Carrie Haase', '/artists/carrie-haase']);
    // An unpublished artist is still credited, without a link (ADR 0042).
    expect(text('.facts li').some((f) => f.includes('Zed Draft'))).toBe(true);
    expect(links.some(([name]) => name === 'Zed Draft')).toBe(false);
  });

  it('offers the other shows, each linking to its own page, and never itself', async () => {
    const { element } = await render(WINTER);

    const cards = [...element.querySelectorAll('app-exhibition-card')];
    expect(cards.length).toBe(1);
    expect(cards[0].querySelector('a')?.getAttribute('href')).toBe('/exhibitions/autumn-salon');
  });

  it('is the not-found page for a show the API does not have', async () => {
    const { element } = await render(undefined);

    expect(element.querySelector('app-not-found')).not.toBeNull();
    expect(element.querySelector('.detail__title')).toBeNull();
  });

  it('is not the not-found page when the read merely failed', async () => {
    const { element, text } = await render(null);

    // The server is answering 503: a blip must not read as "this show is gone" (ADR 0064).
    expect(element.querySelector('app-not-found')).toBeNull();
    expect(text('.prose p')[0]).toContain('could not be loaded');
  });
});
