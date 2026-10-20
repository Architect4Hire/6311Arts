import { Exhibition, ExhibitionSummary } from './exhibition';

/** One draft exhibition as the API returns it, for specs: an evening reception and two credited artists. */
export const EXHIBITION: Exhibition = {
  id: '0199f0a0-0000-7000-8000-0000000000e1',
  title: 'Boats & Pianos',
  slug: 'boats-pianos',
  summary: 'New work: oversized constructions built from reclaimed wood.',
  body: null,
  startDate: '2026-10-24',
  endDate: '2026-12-05',
  reception: {
    startsAt: '2026-10-24T23:00:00+00:00',
    endsAt: '2026-10-25T02:00:00+00:00',
    note: 'Free and open to all',
  },
  displayTimeZone: 'America/Chicago',
  heroMediaId: null,
  artists: [
    { id: 'artist-1', name: 'Brian Eaton', slug: 'brian-eaton', status: 'Published' },
    { id: 'artist-2', name: 'Piloto', slug: 'piloto', status: 'Published' },
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

export function toExhibitionSummary(exhibition: Exhibition): ExhibitionSummary {
  return {
    id: exhibition.id,
    title: exhibition.title,
    slug: exhibition.slug,
    startDate: exhibition.startDate,
    endDate: exhibition.endDate,
    status: exhibition.status,
    heroMediaId: exhibition.heroMediaId,
    artistCount: exhibition.artists.length,
    updatedAt: exhibition.updatedAt,
  };
}
