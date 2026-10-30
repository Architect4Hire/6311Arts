import { provideHttpClient } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ArtClass, Exhibition } from '../../core/models';
import { SiteApi } from '../../services/site-api.service';
import { Classes } from './classes';

const PRINTING: ArtClass = {
  id: '1',
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
};

/** Opens with its reception the same day, so it has no separate "opens" entry. */
const BOATS: Exhibition = {
  slug: 'boats-and-pianos',
  title: 'Boats & Pianos',
  start: '2026-10-24',
  end: '2026-11-28',
  summary: 'Reclaimed wood.',
  artists: [],
  reception: { start: '2026-10-24T18:00:00-05:00', end: '2026-10-24T21:00:00-05:00' },
};

const WINTER: Exhibition = {
  slug: 'winter-show',
  title: 'Winter Show',
  start: '2026-11-14',
  end: '2027-01-09',
  summary: 'Members’ work.',
  artists: [],
  reception: { start: '2026-11-20T18:00:00-06:00', end: '2026-11-20T21:00:00-06:00' },
};

/** A three-week Saturday course whose first session is Oct 24, so Oct 31 is week 2. */
const COURSE: ArtClass = {
  id: '2',
  slug: 'intro-to-screen-printing',
  title: 'Intro to Screen Printing',
  start: '2026-10-24T14:00:00-05:00',
  end: '2026-10-24T17:00:00-05:00',
  sessions: 3,
  type: 'Printmaking',
  level: 'Beginner',
  instructor: 'Gwen Schilling',
  summary: 'Burn a screen.',
};

async function render(query: string, classes: ArtClass[] = [PRINTING]) {
  // The hero's slideshow asks whether motion is reduced; jsdom has no matchMedia.
  vi.stubGlobal('matchMedia', () => ({
    matches: true,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideRouter([]),
      {
        provide: SiteApi,
        useValue: { classes: () => of(classes), exhibitions: () => of([WINTER, BOATS]) },
      },
    ],
  });
  await TestBed.inject(Router).navigateByUrl(`/?${query}`);
  const fixture: ComponentFixture<Classes> = TestBed.createComponent(Classes);
  await fixture.whenStable();
  const element = fixture.nativeElement as HTMLElement;
  /** The calendar cell for a day of the shown month. */
  const day = (n: number) =>
    [...element.querySelectorAll<HTMLElement>('.cal__day:not(.is-out)')].find(
      (d) => d.querySelector('.cal__num')?.textContent?.trim() === String(n),
    )!;
  const text = (el: Element | null | undefined) =>
    el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  return { element, day, text };
}

describe('Classes calendar', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-02T12:00:00-06:00'));
  });
  afterEach(() => vi.useRealTimers());

  it('puts each exhibition’s opening, reception and last day on the calendar, marked apart from classes', async () => {
    const { day, text } = await render('view=calendar&month=2026-11');

    const opens = day(14).querySelector('.cal__event--show');
    expect(text(opens)).toContain('Exhibition opens');
    expect(text(opens)).toContain('Winter Show');
    expect(opens?.getAttribute('href')).toBe('/exhibitions');

    expect(text(day(20).querySelector('.cal__event--show'))).toMatch(
      /Opening reception · 6 pm – 9 pm Winter Show/,
    );
    expect(text(day(28).querySelector('.cal__event--show'))).toContain(
      'Exhibition · last day Boats & Pianos',
    );

    const lesson = day(10).querySelector('.cal__event');
    expect(lesson?.classList).not.toContain('cal__event--show');
    expect(text(lesson)).toContain('Screen Printing Night');
  });

  it('shows a reception on opening day in place of a separate "opens" entry', async () => {
    const { day, text } = await render('view=calendar&month=2026-10');

    const shows = [...day(24).querySelectorAll('.cal__event--show')].map(text);
    expect(shows).toEqual([
      expect.stringMatching(/^Opening reception · 6 pm – 9 pm Boats & Pianos$/),
    ]);
  });

  it('lists the shows on view that month above the grid, with a key', async () => {
    const { element, text } = await render('view=calendar&month=2026-11');

    expect([...element.querySelectorAll('.cal__onview .cal__event--show')].map(text)).toEqual([
      expect.stringContaining('Boats & Pianos'),
      expect.stringContaining('Winter Show'),
    ]);
    expect([...element.querySelectorAll('.cal__key-item')].map(text)).toEqual([
      'Class or event',
      'Exhibition',
    ]);
  });

  it('links each class entry to its instructor', async () => {
    const { day } = await render('view=calendar&month=2026-11');

    const by = day(10).querySelector<HTMLAnchorElement>('.cal__by a');
    expect(by?.textContent?.trim()).toBe('Jane Doe');
    expect(by?.getAttribute('href')).toBe('/instructors/jane-doe');
  });

  it('hides exhibitions once a medium is picked', async () => {
    const { element } = await render('view=calendar&month=2026-11&type=Printmaking');

    expect(element.querySelector('.cal__event--show')).toBeNull();
    expect(element.querySelector('.cal__key')).toBeNull();
    expect(element.querySelector('.cal__event')).not.toBeNull();
  });

  it('keeps the calendar and its exhibitions when a level filter matches no classes', async () => {
    const { element } = await render('view=calendar&month=2026-11&level=Intermediate');

    expect(element.querySelector('.empty')).toBeNull();
    expect(element.querySelector('.cal__event--show')).not.toBeNull();
  });
});

describe('Classes today and this week', () => {
  afterEach(() => vi.useRealTimers());

  const at = (iso: string) => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(iso));
  };

  /** The cards under a "Today" or "This week" heading: exhibition dates read "Exhibition: <moment> <title>". */
  const under = (element: HTMLElement, heading: string) => {
    const h = [...element.querySelectorAll('.soon h2')].find(
      (x) => x.textContent?.trim() === heading,
    );
    return [...(h?.nextElementSibling?.querySelectorAll('.class-card') ?? [])].map((card) =>
      card.classList.contains('class-card--show')
        ? `Exhibition: ${card.querySelector('.class-card__meta span:last-child')?.textContent?.trim()} ${card.querySelector('.class-card__title')?.textContent?.trim()}`
        : `Class: ${card.querySelector('.class-card__title')?.textContent?.trim()}`,
    );
  };

  it('lists an exhibition opening later this week as an exhibition card', async () => {
    at('2026-11-12T12:00:00-06:00');
    const { element } = await render('');

    expect(under(element, 'This week')).toEqual(['Exhibition: Opens Winter Show']);
  });

  it('lists today’s reception until it ends', async () => {
    at('2026-11-20T12:00:00-06:00');
    const { element } = await render('');
    expect(under(element, 'Today')).toEqual(['Exhibition: Opening reception Winter Show']);
    TestBed.resetTestingModule();

    at('2026-11-20T21:30:00-06:00');
    const after = await render('');
    expect(after.element.querySelector('.soon')).toBeNull();
  });

  it('keeps a show’s last day under Today all day', async () => {
    at('2026-11-28T22:00:00-06:00');
    const { element } = await render('');

    expect(under(element, 'Today')).toEqual(['Exhibition: Last day Boats & Pianos']);
  });

  it('lists a later week of a course already under way, on that session’s date', async () => {
    at('2026-10-30T12:00:00-05:00');
    const { element, text } = await render('', [COURSE]);

    expect(under(element, 'This week')).toEqual(['Class: Intro to Screen Printing']);
    const card = element.querySelector('.soon .class-card');
    expect(text(card?.querySelector('.class-card__meta'))).toContain('Week 2 of 3');
    expect(text(card?.querySelector('.class-card__day'))).toBe('31');
  });

  it('lists today’s session of a course until it ends', async () => {
    at('2026-10-31T16:30:00-05:00');
    const { element } = await render('', [COURSE]);
    expect(under(element, 'Today')).toEqual(['Class: Intro to Screen Printing']);
    TestBed.resetTestingModule();

    at('2026-10-31T17:30:00-05:00');
    const after = await render('', [COURSE]);
    expect(after.element.querySelector('.soon')).toBeNull();
  });

  it('lists exhibitions even when a medium is picked, as it does every class', async () => {
    at('2026-11-12T12:00:00-06:00');
    const { element } = await render('type=Printmaking');

    expect(under(element, 'This week')).toEqual(['Exhibition: Opens Winter Show']);
  });
});

describe('Classes grid', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-02T12:00:00-06:00'));
  });
  afterEach(() => vi.useRealTimers());

  /** Each card in the "All events" list, in order: exhibition dates read "Exhibition <moment> <title>". */
  const cards = (element: HTMLElement) =>
    [...element.querySelectorAll('.class-list--full:not(.soon .class-list) > *')].map((card) =>
      card.classList.contains('class-card--show')
        ? `Exhibition: ${card.querySelector('.class-card__meta span:last-child')?.textContent?.trim()} ${card.querySelector('.class-card__title')?.textContent?.trim()}`
        : `Class: ${card.querySelector('.class-card__title')?.textContent?.trim()}`,
    );

  it('sets the upcoming exhibition dates among the classes, by date, as exhibition cards', async () => {
    const { element } = await render('');

    expect(cards(element)).toEqual([
      'Class: Screen Printing Night',
      'Exhibition: Opens Winter Show',
      'Exhibition: Opening reception Winter Show',
      'Exhibition: Last day Boats & Pianos',
      'Exhibition: Last day Winter Show',
    ]);
    const reception = element.querySelectorAll('.class-card--show')[1];
    expect(reception.querySelector('.class-card__when')?.textContent?.replace(/\s+/g, ' ')).toMatch(
      /6 pm – 9 pm · On view Nov 14, 2026 – Jan 9, 2027/,
    );
    expect(reception.querySelector('.class-card__title a')?.getAttribute('href')).toBe(
      '/exhibitions',
    );
  });

  it('leaves exhibitions out once a medium is picked', async () => {
    const { element } = await render('type=Printmaking');

    expect(cards(element)).toEqual(['Class: Screen Printing Night']);
  });

  it('still lists the exhibitions when a level filter matches no classes', async () => {
    const { element } = await render('level=Intermediate');

    expect(element.querySelector('.empty')).toBeNull();
    expect(cards(element)).toHaveLength(4);
  });
});
