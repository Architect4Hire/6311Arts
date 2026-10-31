import {
  SiteClassRun,
  SiteClassSummary,
  SiteExhibitionSummary,
  SiteImage,
  SiteShopItem,
  SiteShopItemSummary,
} from '../models/site';
import {
  paragraphs,
  stripTags,
  toArtClasses,
  toArtistPage,
  toExhibition,
  toExhibitions,
  toMembershipTier,
  toProduct,
  toProductPage,
  zonedIso,
} from './site-mappers';

const CHICAGO = 'America/Chicago';

const IMAGE: SiteImage = {
  src: '/media/abc/w1600.jpg?v=1',
  alt: 'A screen print drying',
  width: 1600,
  height: 1200,
  sources: [{ url: '/media/abc/w1600.jpg?v=1', width: 1600, mimeType: 'image/jpeg' }],
};

function run(overrides: Partial<SiteClassRun> = {}): SiteClassRun {
  return {
    id: 'run-1',
    label: null,
    instructor: { name: 'Jane Doe', slug: 'jane-doe' },
    cadence: 'Tuesdays',
    displayTimeZone: CHICAGO,
    startsAt: '2026-11-10T00:00:00+00:00',
    endsAt: '2026-12-15T03:00:00+00:00',
    sessionCount: 6,
    capacity: 12,
    priceCents: 4500,
    currency: 'USD',
    seatsRemaining: 3,
    soldOut: false,
    sessions: [
      { id: 's1', startsAt: '2026-11-10T00:00:00+00:00', endsAt: '2026-11-10T03:00:00+00:00' },
      { id: 's2', startsAt: '2026-11-17T00:00:00+00:00', endsAt: '2026-11-17T03:00:00+00:00' },
    ],
    ...overrides,
  };
}

function offering(overrides: Partial<SiteClassSummary> = {}): SiteClassSummary {
  return {
    id: 'class-1',
    slug: 'screen-printing',
    title: 'Screen Printing',
    summary: 'Pull a print.',
    classType: 'printmaking',
    level: 'Beginner',
    image: IMAGE,
    fromPriceCents: 4500,
    currency: 'USD',
    runs: [run()],
    ...overrides,
  };
}

function item(overrides: Partial<SiteShopItemSummary> = {}): SiteShopItemSummary {
  return {
    id: '0b4c…',
    slug: 'copper-cuff',
    title: 'Copper Cuff',
    artist: { name: 'Carrie Haase', slug: 'carrie-haase' },
    categories: ['jewelry'],
    image: IMAGE,
    priceCents: 26500,
    currency: 'USD',
    oneOfAKind: true,
    soldOut: false,
    ...overrides,
  };
}

describe('zonedIso', () => {
  it('reads a UTC instant as the gallery wall clock, with the offset of the day', () => {
    // Central Standard Time in November, Central Daylight Time in October.
    expect(zonedIso('2026-11-10T00:00:00+00:00', CHICAGO)).toBe('2026-11-09T18:00:00-06:00');
    expect(zonedIso('2026-10-22T23:00:00+00:00', CHICAGO)).toBe('2026-10-22T18:00:00-05:00');
  });

  it('crosses the November change on the right side of it', () => {
    // 1 November 2026, 2 am CDT becomes 1 am CST: 06:30 UTC is 1:30 CDT, 07:30 UTC is 1:30 CST.
    expect(zonedIso('2026-11-01T06:30:00+00:00', CHICAGO)).toBe('2026-11-01T01:30:00-05:00');
    expect(zonedIso('2026-11-01T07:30:00+00:00', CHICAGO)).toBe('2026-11-01T01:30:00-06:00');
  });

  it('leaves something that is not a date alone', () => {
    expect(zonedIso('not a date', CHICAGO)).toBe('not a date');
  });
});

describe('toArtClasses', () => {
  it('makes one entry per run, dated by its first session in Chicago time, with the run’s own money and seats', () => {
    const [c] = toArtClasses([offering()]);

    expect(c).toEqual({
      id: 'run-1',
      slug: 'screen-printing',
      title: 'Screen Printing',
      start: '2026-11-09T18:00:00-06:00',
      end: '2026-11-09T21:00:00-06:00',
      sessions: 6,
      cadence: 'Tuesdays',
      type: 'Printmaking',
      level: 'Beginner',
      price: 4500,
      instructor: 'Jane Doe',
      image: IMAGE.src,
      summary: 'Pull a print.',
      spotsLeft: 3,
      capacity: 12,
    });
  });

  it('orders every run of every class soonest first', () => {
    const later = run({
      id: 'run-2',
      sessions: [
        { id: 's9', startsAt: '2027-01-12T00:00:00+00:00', endsAt: '2027-01-12T03:00:00+00:00' },
      ],
    });
    const other = offering({
      slug: 'drawing',
      runs: [
        run({
          id: 'run-3',
          sessions: [
            {
              id: 's5',
              startsAt: '2026-11-03T00:00:00+00:00',
              endsAt: '2026-11-03T03:00:00+00:00',
            },
          ],
        }),
      ],
    });

    expect(toArtClasses([offering({ runs: [later, run()] }), other]).map((c) => c.id)).toEqual([
      'run-3',
      'run-1',
      'run-2',
    ]);
  });

  it('leaves Square’s figures out until Square has reported them, and reads a sold-out flag as none left', () => {
    const [unpriced] = toArtClasses([
      offering({ runs: [run({ priceCents: null, seatsRemaining: null })] }),
    ]);
    expect(unpriced.price).toBeUndefined();
    expect(unpriced.spotsLeft).toBeUndefined();

    const [soldOut] = toArtClasses([
      offering({ runs: [run({ seatsRemaining: null, soldOut: true })] }),
    ]);
    expect(soldOut.spotsLeft).toBe(0);
  });

  it('files a type or level the spike never had under Other and All levels', () => {
    const [c] = toArtClasses([
      offering({
        classType: 'Ceramics',
        level: null,
        runs: [run({ instructor: null, cadence: null })],
      }),
    ]);
    expect([c.type, c.level, c.instructor, c.cadence]).toEqual([
      'Other',
      'All levels',
      undefined,
      undefined,
    ]);
  });

  it('dates a run with no sessions by the run itself', () => {
    const [c] = toArtClasses([offering({ runs: [run({ sessions: [] })] })]);
    expect([c.start, c.end]).toEqual(['2026-11-09T18:00:00-06:00', '2026-12-14T21:00:00-06:00']);
  });
});

describe('toExhibitions', () => {
  const show = (
    slug: string,
    startDate: string,
    overrides: Partial<SiteExhibitionSummary> = {},
  ): SiteExhibitionSummary => ({
    id: slug,
    slug,
    title: slug,
    summary: null,
    startDate,
    endDate: '2027-01-30',
    reception: null,
    displayTimeZone: CHICAGO,
    artists: [],
    image: null,
    ...overrides,
  });

  it('keeps the calendar dates, names every credited artist and puts the newest opening first', () => {
    const list = toExhibitions([
      show('autumn', '2026-09-01'),
      show('winter', '2026-11-14', {
        summary: 'Members’ work.',
        artists: [
          { name: 'Jane Doe', slug: 'jane-doe' },
          { name: 'Zed Draft', slug: null },
        ],
        image: IMAGE,
        reception: {
          startsAt: '2026-11-21T00:00:00+00:00',
          endsAt: '2026-11-21T03:00:00+00:00',
          note: null,
        },
      }),
    ]);

    expect(list.map((e) => e.slug)).toEqual(['winter', 'autumn']);
    expect(list[0]).toEqual({
      slug: 'winter',
      title: 'winter',
      start: '2026-11-14',
      end: '2027-01-30',
      summary: 'Members’ work.',
      artists: ['Jane Doe', 'Zed Draft'],
      // The same credits with their pages, for the show's own page: an unpublished artist has no slug (ADR 0064).
      credits: [{ name: 'Jane Doe', slug: 'jane-doe' }, { name: 'Zed Draft' }],
      image: IMAGE.src,
      reception: {
        start: '2026-11-20T18:00:00-06:00',
        end: '2026-11-20T21:00:00-06:00',
        note: undefined,
      },
    });
    expect(list[1].summary).toBe('');
  });

  it('reads the show’s own write-up as paragraphs on its page', () => {
    const page = toExhibition({
      ...show('winter', '2026-11-14', { summary: 'Members’ work.' }),
      bodyText: 'Nine painters.\n\n  Open late on Fridays.  \n\n\n',
    });

    expect(page.body).toEqual(['Nine painters.', 'Open late on Fridays.']);
    expect(toExhibition({ ...show('autumn', '2026-09-01'), bodyText: null }).body).toEqual([]);
  });
});

describe('toProduct', () => {
  it('names the piece, its lead photograph and Square’s price, and reads sold out as not in stock', () => {
    expect(toProduct(item({ soldOut: true }))).toEqual({
      id: '0b4c…',
      slug: 'copper-cuff',
      name: 'Copper Cuff',
      artist: { name: 'Carrie Haase', slug: 'carrie-haase' },
      price: 26500,
      image: IMAGE.src,
      categories: ['jewelry'],
      inStock: false,
      description: undefined,
    });
  });

  it('credits an artist with no page by name only, and leaves an unpriced piece without a price', () => {
    const p = toProduct(
      item({ artist: { name: 'Zed Draft', slug: null }, priceCents: null, image: null }),
    );
    expect(p.artist).toEqual({ name: 'Zed Draft' });
    expect(p.price).toBeUndefined();
    expect(p.image).toBeUndefined();
  });

  it('turns a piece’s description into one line of text', () => {
    const piece: SiteShopItem = {
      ...item(),
      descriptionHtml: '<p>Hammered &amp; oxidized.</p><p>One of a kind.</p>',
      images: [IMAGE],
    };
    expect(toProduct(piece).description).toBe('Hammered & oxidized. One of a kind.');
    expect(toProduct({ ...piece, descriptionHtml: '<p> </p>' }).description).toBeUndefined();
  });

  it('carries the page’s total and cursor beside its works', () => {
    expect(toProductPage({ items: [item()], nextCursor: 'abc', totalCount: 312 })).toEqual({
      items: [toProduct(item())],
      total: 312,
      nextCursor: 'abc',
    });
  });
});

describe('toArtistPage', () => {
  it('carries the CMS’s portrait, medium, bio and gallery status beside the first page of their work', () => {
    const page = toArtistPage({
      artist: {
        id: 'a1',
        slug: 'carrie-haase',
        name: 'Carrie Haase',
        medium: 'Jewelry',
        active: true,
        portrait: IMAGE,
        bioText: 'Metalsmith in Edgewater.\n\nShe hammers copper.',
      },
      works: { items: [item()], nextCursor: 'abc' },
    });
    expect(page).toEqual({
      artist: {
        slug: 'carrie-haase',
        name: 'Carrie Haase',
        medium: 'Jewelry',
        image: IMAGE.src,
        bio: ['Metalsmith in Edgewater.', 'She hammers copper.'],
        active: true,
      },
      works: [toProduct(item())],
      nextCursor: 'abc',
    });
  });

  it('leaves out what staff have not filled in', () => {
    const { artist } = toArtistPage({
      artist: {
        id: 'a2',
        slug: 'zed',
        name: 'Zed',
        medium: null,
        active: false,
        portrait: null,
        bioText: null,
      },
      works: { items: [], nextCursor: null },
    });
    expect(artist.medium).toBeUndefined();
    expect(artist.image).toBeUndefined();
    expect(artist.bio).toEqual([]);
  });
});

describe('paragraphs', () => {
  it('splits at blank lines, Windows ones and whitespace-only ones included, and drops the empty', () => {
    expect(paragraphs('One\nstill one.\r\n\r\nTwo.\n  \nThree.\n\n\n')).toEqual([
      'One\nstill one.',
      'Two.',
      'Three.',
    ]);
    expect(paragraphs('   ')).toEqual([]);
    expect(paragraphs(null)).toEqual([]);
  });
});

describe('toMembershipTier', () => {
  it('shows the CMS’s copy and Square’s price, which is absent until Square has one', () => {
    const plan = {
      id: 'plan-1',
      slug: 'black-membership',
      name: 'Black',
      perks: ['10% off every class & workshop'],
      discountPercent: 10,
      featured: true,
      priceCents: 30000,
      currency: 'USD',
      cadence: 'ANNUAL',
    };
    expect(toMembershipTier(plan)).toEqual({
      id: 'plan-1',
      slug: 'black-membership',
      name: 'Black',
      price: 30000,
      perks: ['10% off every class & workshop'],
      discount: 10,
      featured: true,
    });
    expect(toMembershipTier({ ...plan, priceCents: null }).price).toBeUndefined();
  });
});

describe('stripTags', () => {
  it('keeps the words and drops the markup', () => {
    expect(stripTags('<h2>Care</h2><ul><li>Dry&nbsp;cloth</li></ul>')).toBe('Care Dry cloth');
  });
});
