import { ClassOffering, ClassSummary } from './class';

/**
 * One draft class as the API returns it, for specs: one run taught by Gwen, of two sittings, not in Square yet
 * (ADR 0044).
 */
export const CLASS_OFFERING: ClassOffering = {
  id: '0199f0a0-0000-7000-8000-0000000000c1',
  title: 'Intro to Screen Printing',
  slug: 'intro-to-screen-printing',
  summary: 'Burn your first screen and pull a full edition of prints in five weeks.',
  body: null,
  classType: 'Printmaking',
  level: 'Beginner',
  offeringNumber: 4,
  heroMediaId: null,
  imageMediaIds: [],
  runs: [
    {
      id: '0199f0a0-0000-7000-8000-0000000000d1',
      label: 'Saturday afternoons',
      instructor: {
        id: '0199f0a0-0000-7000-8000-0000000000e1',
        name: 'Gwen Schilling',
        slug: 'gwen-schilling',
        status: 'Published',
      },
      cadence: 'Saturdays',
      displayTimeZone: 'America/Chicago',
      sku: 'CLS-4-1',
      capacity: 10,
      startingPriceCents: 30000,
      currency: 'USD',
      startingPriceEditable: true,
      priceCents: null,
      seatsRemaining: null,
      soldOut: false,
      squareHasRecord: false,
      sessions: [
        {
          id: '0199f0a0-0000-7000-8000-0000000000c2',
          startsAt: '2026-10-24T19:00:00+00:00',
          endsAt: '2026-10-24T22:00:00+00:00',
        },
        {
          id: '0199f0a0-0000-7000-8000-0000000000c3',
          startsAt: '2026-10-31T19:00:00+00:00',
          endsAt: '2026-10-31T22:00:00+00:00',
        },
      ],
    },
  ],
  status: 'Draft',
  publishedAt: null,
  scheduledFor: null,
  slugLocked: false,
  legacyWordPressId: null,
  createdAt: '2026-10-20T12:00:00+00:00',
  updatedAt: '2026-10-20T12:00:00+00:00',
  updatedBy: 'editor-1',
  etag: '"0123456789abcdef0123456789abcdef"',
};

/** The same class after its first sync: Square holds the run's price and four seats are taken. */
export const SYNCED_CLASS: ClassOffering = {
  ...CLASS_OFFERING,
  runs: CLASS_OFFERING.runs.map((run) => ({
    ...run,
    startingPriceEditable: false,
    priceCents: 32500,
    seatsRemaining: 6,
    squareHasRecord: true,
  })),
};

export function toClassSummary(offering: ClassOffering): ClassSummary {
  return {
    id: offering.id,
    title: offering.title,
    slug: offering.slug,
    status: offering.status,
    classType: offering.classType,
    level: offering.level,
    instructors: [
      ...new Set(offering.runs.flatMap((run) => (run.instructor ? [run.instructor.name] : []))),
    ],
    heroMediaId: offering.heroMediaId,
    runCount: offering.runs.length,
    nextSessionStartsAt: offering.runs[0]?.sessions[0]?.startsAt ?? null,
    updatedAt: offering.updatedAt,
  };
}
