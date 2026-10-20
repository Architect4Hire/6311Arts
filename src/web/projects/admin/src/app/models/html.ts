/**
 * The server's HTML allow-list as the admin's editor needs it (`HtmlPolicy.cs`, ADR 0034), so the editor never
 * produces what the API would strip. The API sanitizes regardless and has the last word.
 */

const LINK_SCHEMES = ['http:', 'https:', 'mailto:', 'tel:'];

/**
 * Whether a link target is one a stored body may carry (`HtmlPolicy.IsAllowedLink`): this site (`/…`, never `//…` or
 * `/\…`), a place on the page (`#…`), or a web, email or phone address.
 */
export function isAllowedLink(href: string): boolean {
  if (href === '' || /[\s\p{Cc}]/u.test(href)) return false;
  if (href.startsWith('#')) return href.length > 1;
  if (href.startsWith('/')) return href.length === 1 || (href[1] !== '/' && href[1] !== '\\');

  let url: URL;
  try {
    url = new URL(href);
  } catch {
    return false;
  }
  if (!LINK_SCHEMES.includes(url.protocol)) return false;
  return url.protocol === 'mailto:' || url.protocol === 'tel:' || url.hostname !== '';
}

/** The rendition a body shows a library image at: wide enough for a page column, small enough to load fast. */
export const BODY_IMAGE_RENDITION = '800.webp';

/** An image source the allow-list keeps: a library rendition, site-relative (`HtmlPolicy.TryParseMediaImage`). */
export function mediaImageSrc(assetId: string): string {
  return `/media/${encodeURIComponent(assetId)}/${BODY_IMAGE_RENDITION}`;
}

const MEDIA_IMAGE =
  /^\/media\/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\/(?:400|800|1600)\.(?:webp|jpg)(?:\?v=[0-9a-f]{1,64})?$/;

/** As `HtmlPolicy.TryParseMediaImage`: Media's own rendition widths only (`MediaPolicy.RenditionWidths`). */
export function isMediaImageSrc(src: string): boolean {
  return MEDIA_IMAGE.test(src);
}
