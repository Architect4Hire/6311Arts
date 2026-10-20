import { ClassOffering, ClassSummary } from './class';

/** One draft class as the API returns it, for specs: two sittings, neither of them in Square yet. */
export const CLASS_OFFERING: ClassOffering = {
  id: '0199f0a0-0000-7000-8000-0000000000c1',
  title: 'Intro to Screen Printing',
  slug: 'intro-to-screen-printing',
  summary: 'Burn your first screen and pull a full edition of prints in five weeks.',
  body: null,
  classType: 'Printmaking',
  level: 'Beginner',
  instructor: 'Gwen Schilling',
  cadence: 'Saturdays',
  startingPriceCents: 30000,
  currency: 'USD',
  startingPriceEditable: true,
  offeringNumber: 4,
  heroMediaId: null,
  imageMediaIds: [],
  sessions: [
    {
      id: '0199f0a0-0000-7000-8000-0000000000c2',
      startsAt: '2026-10-24T19:00:00+00:00',
      endsAt: '2026-10-24T22:00:00+00:00',
      displayTimeZone: 'America/Chicago',
      sku: 'CLS-4-1',
      capacity: 10,
      priceCents: null,
      seatsRemaining: null,
      soldOut: false,
      squareHasRecord: false,
    },
    {
      id: '0199f0a0-0000-7000-8000-0000000000c3',
      startsAt: '2026-10-31T19:00:00+00:00',
      endsAt: '2026-10-31T22:00:00+00:00',
      displayTimeZone: 'America/Chicago',
      sku: 'CLS-4-2',
      capacity: 10,
      priceCents: null,
      seatsRemaining: null,
      soldOut: false,
      squareHasRecord: false,
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

/** The same class after its first sync: Square holds the price and four seats are taken. */
export const SYNCED_CLASS: ClassOffering = {
  ...CLASS_OFFERING,
  startingPriceEditable: false,
  sessions: CLASS_OFFERING.sessions.map((session) => ({
    ...session,
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
    instructor: offering.instructor,
    heroMediaId: offering.heroMediaId,
    sessionCount: offering.sessions.length,
    nextSessionStartsAt: offering.sessions[0]?.startsAt ?? null,
    updatedAt: offering.updatedAt,
  };
}
