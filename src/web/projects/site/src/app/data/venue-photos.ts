/**
 * Photographs of the building, galleries, classroom and shop, copied from the 6311arts.com media library
 * on 2026-10-18 (`/wp-json/wp/v2/media`) and downloaded on 2026-10-31 at the Jetpack CDN's 1600×1100 fit
 * (EXIF stripped) into `public/img/`, so the site serves them itself.
 *
 * Heroes fall back to these when the page has no content photo of its own, so no page ever renders a flat
 * dark banner. Replace them with CMS media once Arts6311.Importer runs; the paths are the only thing to change.
 */
const photo = (file: string) => `/img/${file}`;

/** The one photo that opens the site: a full room mid-event, art on every wall. */
export const HERO_PHOTO = photo('gallery6.jpg');

/** The East and West galleries hung for a show, empty enough to read the work on the walls. */
export const GALLERY_PHOTOS = [
  photo('gallery7.jpg'),
  photo('6311arts-Gallery-INT.jpeg'),
  photo('gallery4.jpg'),
  HERO_PHOTO,
];

/** Opening receptions and talks, with a crowd in them. */
export const EVENT_PHOTOS = [
  photo('IMG_2064.jpeg'),
  photo('IMG_2043.jpeg'),
  photo('IMG_2028.jpeg'),
];

/** The classroom and print studio mid-workshop, work spread across the tables. */
export const STUDIO_PHOTOS = [
  photo('IMG_1130.jpeg'),
  photo('gallery8.jpg'),
  photo('IMG_1141.jpeg'),
  photo('gallery1.jpg'),
];

/** The shop floor and the artists who fill it. */
export const SHOP_PHOTOS = [photo('gallery2.jpg'), photo('galleryyy.jpg'), photo('gallery3.jpg')];

/** The blue storefront on Clark Street. */
export const FRONT_PHOTOS = [photo('6311arts-FRONT.jpeg'), photo('gallery9.jpg')];

/** The default hero rotation: one of each, so the slideshow never repeats a room twice in a row. */
export const VENUE_PHOTOS = [
  HERO_PHOTO,
  EVENT_PHOTOS[0],
  STUDIO_PHOTOS[0],
  FRONT_PHOTOS[0],
  GALLERY_PHOTOS[0],
  SHOP_PHOTOS[0],
];
