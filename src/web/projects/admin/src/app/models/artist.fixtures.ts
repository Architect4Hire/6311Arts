import { Artist, ArtistSummary } from './artist';

/** One draft artist as the API returns it, for specs. */
export const ARTIST: Artist = {
  id: '0199f0a0-0000-7000-8000-000000000001',
  name: 'Carrie Haase',
  slug: 'carrie-haase',
  medium: 'Jewelry',
  bio: 'Metalsmith working in sterling and stone.',
  active: true,
  portraitMediaId: null,
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

export function toArtistSummary(artist: Artist): ArtistSummary {
  return {
    id: artist.id,
    name: artist.name,
    slug: artist.slug,
    medium: artist.medium,
    active: artist.active,
    status: artist.status,
    portraitMediaId: artist.portraitMediaId,
    updatedAt: artist.updatedAt,
  };
}
