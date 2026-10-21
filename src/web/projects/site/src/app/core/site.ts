/**
 * The site's own pictures in Media (prompt 3.3): MigrationService seeds them at these fixed ids, published, in every
 * environment (`MediaSiteImages` in the Domain; a test keeps the two in step). Named by rendition, because Media never
 * serves a master to a visitor.
 */
export const SITE_IMAGES = {
  logo: '6311a0f5-0000-4000-8000-000000000001',
  openGraph: '6311a0f5-0000-4000-8000-000000000002',
} as const;

/** Everything about the organization that appears on more than one page. */
export const SITE = {
  name: '6311arts',
  tagline: 'art & community',
  url: 'https://6311arts.com',
  description:
    'An inclusive art center on Chicago’s North Side. Gallery, shop and classes taught by local artists in a former funeral home at 6311 N. Clark St.',
  phone: '872-370-4500',
  email: 'info@6311arts.com',
  instagram: 'https://www.instagram.com/6311arts',
  address: {
    street: '6311 N. Clark St.',
    city: 'Chicago',
    region: 'IL',
    postal: '60660',
    neighborhood: 'Rogers Edge, Edgewater',
  },
  geo: { lat: 41.9973, lng: -87.6699 },
  mapUrl: 'https://maps.google.com/?q=6311+N+Clark+St,+Chicago,+IL+60660',
  hours: [
    { days: 'Mon – Tue', time: 'By appointment', schema: null },
    {
      days: 'Wednesday',
      time: '11 am – 5 pm',
      schema: { days: ['Wednesday'], opens: '11:00', closes: '17:00' },
    },
    {
      days: 'Thu – Sat',
      time: '11 am – 6 pm',
      schema: { days: ['Thursday', 'Friday', 'Saturday'], opens: '11:00', closes: '18:00' },
    },
    {
      days: 'Sunday',
      time: '11 am – 5 pm',
      schema: { days: ['Sunday'], opens: '11:00', closes: '17:00' },
    },
  ],
  founded: 2025,
  /** The picture a page shares when it has none of its own (Open Graph, X), absolute because crawlers need it so. */
  ogImage: `https://6311arts.com/media/${SITE_IMAGES.openGraph}/1600.jpg`,
  /** The logo the Organization structured data names: a WebP rendition, which keeps the line logo's transparency. */
  logo: `https://6311arts.com/media/${SITE_IMAGES.logo}/800.webp`,
} as const;
