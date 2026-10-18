/**
 * Photographs of the building, galleries, classroom and shop, copied from the 6311arts.com media library
 * on 2026-10-18 (`/wp-json/wp/v2/media`). They are served in full colour through the site's Jetpack CDN.
 *
 * Heroes fall back to these when the page has no content photo of its own, so no page ever renders a flat
 * dark banner. Replace them with CMS media once Arts6311.Importer runs; the paths are the only thing to change.
 */
const photo = (path: string, size = 'fit=1600%2C1100') =>
  `https://i0.wp.com/6311arts.com/wp-content/uploads/${path}?${size}&ssl=1`;

/** The one photo that opens the site: a full room mid-event, art on every wall. */
export const HERO_PHOTO = photo('2026/07/gallery6.jpg');

/** The East and West galleries hung for a show, empty enough to read the work on the walls. */
export const GALLERY_PHOTOS = [
  photo('2026/07/gallery7.jpg'),
  photo('2026/08/6311arts-Gallery-INT.jpeg'),
  photo('2026/07/gallery4.jpg'),
  HERO_PHOTO,
];

/** Opening receptions and talks, with a crowd in them. */
export const EVENT_PHOTOS = [
  photo('2026/08/IMG_2064.jpeg'),
  photo('2026/08/IMG_2043.jpeg'),
  photo('2026/08/IMG_2028.jpeg'),
];

/** The classroom and print studio mid-workshop, work spread across the tables. */
export const STUDIO_PHOTOS = [
  photo('2026/08/IMG_1130.jpeg'),
  photo('2026/07/gallery8.jpg'),
  photo('2026/08/IMG_1141.jpeg'),
  photo('2026/07/gallery1.jpg'),
];

/** The shop floor and the artists who fill it. */
export const SHOP_PHOTOS = [
  photo('2026/07/gallery2.jpg'),
  photo('2026/07/galleryyy.jpg'),
  photo('2026/07/gallery3.jpg'),
];

/** The blue storefront on Clark Street. */
export const FRONT_PHOTOS = [photo('2026/08/6311arts-FRONT.jpeg'), photo('2026/07/gallery9.jpg')];

/** The default hero rotation: one of each, so the slideshow never repeats a room twice in a row. */
export const VENUE_PHOTOS = [
  HERO_PHOTO,
  EVENT_PHOTOS[0],
  STUDIO_PHOTOS[0],
  FRONT_PHOTOS[0],
  GALLERY_PHOTOS[0],
  SHOP_PHOTOS[0],
];
