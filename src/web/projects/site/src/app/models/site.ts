/**
 * The site read API's shapes (`/api/v1/site/*`, ADR 0042), mirroring the `Site*ServiceModel`s exactly (the OpenAPI
 * snapshot): string ids, integer cents with a currency, UTC instants written `+00:00`, calendar dates as
 * `YYYY-MM-DD`. Square's record of the money (`priceCents`, `seatsRemaining`, `soldOut`) is `null` until Square has
 * reported it. Each model has a decoder; the pages never see these, only what `site-mappers.ts` makes of them.
 */

import {
  decodeArray,
  decodeNullable,
  fields,
  isBoolean,
  isNullableNumber,
  isNullableString,
  isNumber,
  isString,
} from './decode';

export interface SiteImageSource {
  readonly url: string;
  readonly width: number;
  readonly mimeType: string;
}

/** One picture: the widest JPEG rendition as `src`, every rendition in `sources`, never the master. */
export interface SiteImage {
  readonly src: string;
  readonly alt: string;
  readonly width: number;
  readonly height: number;
  readonly sources: readonly SiteImageSource[];
}

/** An artist or instructor named on another record; `slug` is `null` when they have no published page. */
export interface SiteCredit {
  readonly name: string;
  readonly slug: string | null;
}

export interface SiteClassSession {
  readonly id: string;
  readonly startsAt: string;
  readonly endsAt: string;
}

export interface SiteClassRun {
  readonly id: string;
  readonly label: string | null;
  readonly instructor: SiteCredit | null;
  readonly cadence: string | null;
  readonly displayTimeZone: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly sessionCount: number;
  readonly capacity: number;
  readonly priceCents: number | null;
  readonly currency: string;
  readonly seatsRemaining: number | null;
  readonly soldOut: boolean;
  readonly sessions: readonly SiteClassSession[];
}

export interface SiteClassSummary {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly summary: string | null;
  readonly classType: string | null;
  readonly level: string | null;
  readonly image: SiteImage | null;
  readonly fromPriceCents: number | null;
  readonly currency: string;
  readonly runs: readonly SiteClassRun[];
}

/**
 * `GET /site/classes/{slug}`: one class as its page renders it — the list's fields, its "More details" body, and
 * the runs still to come, which is **empty once the class is over** and before any dates are set.
 */
export interface SiteClass extends SiteClassSummary {
  readonly bodyHtml: string | null;
}

export interface SiteInstructor {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly medium: string | null;
  readonly active: boolean;
  readonly portrait: SiteImage | null;
  readonly bio: string | null;
  readonly website: string | null;
  readonly instagram: string | null;
  readonly artistSlug: string | null;
}

export interface SiteExhibitionReception {
  readonly startsAt: string;
  readonly endsAt: string;
  readonly note: string | null;
}

export interface SiteExhibitionSummary {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly summary: string | null;
  readonly startDate: string;
  readonly endDate: string;
  readonly reception: SiteExhibitionReception | null;
  readonly displayTimeZone: string;
  readonly artists: readonly SiteCredit[];
  readonly image: SiteImage | null;
}

/**
 * `GET /site/exhibitions/{slug}`: one show as its page renders it — the list's fields and the body.
 *
 * `bodyText` is plain text; blank lines separate paragraphs (it is not HTML, as a class's or a page's body is).
 */
export interface SiteExhibition extends SiteExhibitionSummary {
  readonly bodyText: string | null;
}

export interface SiteArtistSummary {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly medium: string | null;
  readonly active: boolean;
  readonly portrait: SiteImage | null;
  readonly workCount: number;
}

/** One published artist as their page shows them. `bioText` is plain text; blank lines separate paragraphs. */
export interface SiteArtist {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly medium: string | null;
  readonly active: boolean;
  readonly portrait: SiteImage | null;
  readonly bioText: string | null;
}

/** `GET /site/artists/{slug}`: the artist, and the first page of their work, which continues at `/site/shop?artist=`. */
export interface SiteArtistPage {
  readonly artist: SiteArtist;
  readonly works: {
    readonly items: readonly SiteShopItemSummary[];
    readonly nextCursor: string | null;
  };
}

export interface SiteShopItemSummary {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly artist: SiteCredit | null;
  readonly categories: readonly string[];
  readonly image: SiteImage | null;
  readonly priceCents: number | null;
  readonly currency: string;
  readonly oneOfAKind: boolean;
  readonly soldOut: boolean;
}

export interface SiteShopItem extends SiteShopItemSummary {
  readonly descriptionHtml: string | null;
  readonly images: readonly SiteImage[];
}

/** `GET /site/shop`: one page, where to resume (`null` on the last), and how many works the filters match in all. */
export interface SiteShopPage {
  readonly items: readonly SiteShopItemSummary[];
  readonly nextCursor: string | null;
  readonly totalCount: number;
}

export interface SiteMembershipPlan {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly perks: readonly string[];
  readonly discountPercent: number;
  readonly featured: boolean;
  readonly priceCents: number | null;
  readonly currency: string;
  readonly cadence: string | null;
}

export interface SitePage {
  readonly id: string;
  readonly slug: string;
  readonly title: string;
  readonly bodyHtml: string | null;
  readonly updatedAt: string;
}

// ---- Decoders --------------------------------------------------------------------------------------------------

export function decodeSiteImage(body: unknown): SiteImage | null {
  const f = fields(body);
  if (
    !f ||
    !isString(f['src']) ||
    !isString(f['alt']) ||
    !isNumber(f['width']) ||
    !isNumber(f['height'])
  )
    return null;
  const sources = decodeArray(f['sources'], (item) => {
    const s = fields(item);
    return s && isString(s['url']) && isNumber(s['width']) && isString(s['mimeType'])
      ? { url: s['url'], width: s['width'], mimeType: s['mimeType'] }
      : null;
  });
  return sources
    ? { src: f['src'], alt: f['alt'], width: f['width'], height: f['height'], sources }
    : null;
}

export function decodeSiteCredit(body: unknown): SiteCredit | null {
  const f = fields(body);
  return f && isString(f['name']) && isNullableString(f['slug'])
    ? { name: f['name'], slug: f['slug'] }
    : null;
}

function decodeSession(body: unknown): SiteClassSession | null {
  const f = fields(body);
  return f && isString(f['id']) && isString(f['startsAt']) && isString(f['endsAt'])
    ? { id: f['id'], startsAt: f['startsAt'], endsAt: f['endsAt'] }
    : null;
}

export function decodeSiteClassRun(body: unknown): SiteClassRun | null {
  const f = fields(body);
  if (!f) return null;
  const instructor = decodeNullable(f['instructor'], decodeSiteCredit);
  const sessions = decodeArray(f['sessions'], decodeSession);
  const {
    id,
    label,
    cadence,
    displayTimeZone,
    startsAt,
    endsAt,
    sessionCount,
    capacity,
    priceCents,
    currency,
    seatsRemaining,
    soldOut,
  } = f;
  return isString(id) &&
    isNullableString(label) &&
    instructor !== undefined &&
    isNullableString(cadence) &&
    isString(displayTimeZone) &&
    isString(startsAt) &&
    isString(endsAt) &&
    isNumber(sessionCount) &&
    isNumber(capacity) &&
    isNullableNumber(priceCents) &&
    isString(currency) &&
    isNullableNumber(seatsRemaining) &&
    isBoolean(soldOut) &&
    sessions
    ? {
        id,
        label,
        instructor,
        cadence,
        displayTimeZone,
        startsAt,
        endsAt,
        sessionCount,
        capacity,
        priceCents,
        currency,
        seatsRemaining,
        soldOut,
        sessions,
      }
    : null;
}

export function decodeSiteClassSummary(body: unknown): SiteClassSummary | null {
  const f = fields(body);
  if (!f) return null;
  const image = decodeNullable(f['image'], decodeSiteImage);
  const runs = decodeArray(f['runs'], decodeSiteClassRun);
  const { id, slug, title, summary, classType, level, fromPriceCents, currency } = f;
  return isString(id) &&
    isString(slug) &&
    isString(title) &&
    isNullableString(summary) &&
    isNullableString(classType) &&
    isNullableString(level) &&
    image !== undefined &&
    isNullableNumber(fromPriceCents) &&
    isString(currency) &&
    runs
    ? { id, slug, title, summary, classType, level, image, fromPriceCents, currency, runs }
    : null;
}

export function decodeSiteClass(body: unknown): SiteClass | null {
  const summary = decodeSiteClassSummary(body);
  const f = fields(body);
  if (!summary || !f || !isNullableString(f['bodyHtml'])) return null;
  return { ...summary, bodyHtml: f['bodyHtml'] };
}

export function decodeSiteInstructor(body: unknown): SiteInstructor | null {
  const f = fields(body);
  if (!f) return null;
  const portrait = decodeNullable(f['portrait'], decodeSiteImage);
  const { id, slug, name, medium, active, bio, website, instagram, artistSlug } = f;
  return isString(id) &&
    isString(slug) &&
    isString(name) &&
    isNullableString(medium) &&
    isBoolean(active) &&
    portrait !== undefined &&
    isNullableString(bio) &&
    isNullableString(website) &&
    isNullableString(instagram) &&
    isNullableString(artistSlug)
    ? { id, slug, name, medium, active, portrait, bio, website, instagram, artistSlug }
    : null;
}

function decodeReception(body: unknown): SiteExhibitionReception | null {
  const f = fields(body);
  return f && isString(f['startsAt']) && isString(f['endsAt']) && isNullableString(f['note'])
    ? { startsAt: f['startsAt'], endsAt: f['endsAt'], note: f['note'] }
    : null;
}

export function decodeSiteExhibitionSummary(body: unknown): SiteExhibitionSummary | null {
  const f = fields(body);
  if (!f) return null;
  const reception = decodeNullable(f['reception'], decodeReception);
  const artists = decodeArray(f['artists'], decodeSiteCredit);
  const image = decodeNullable(f['image'], decodeSiteImage);
  const { id, slug, title, summary, startDate, endDate, displayTimeZone } = f;
  return isString(id) &&
    isString(slug) &&
    isString(title) &&
    isNullableString(summary) &&
    isString(startDate) &&
    isString(endDate) &&
    reception !== undefined &&
    isString(displayTimeZone) &&
    artists &&
    image !== undefined
    ? { id, slug, title, summary, startDate, endDate, reception, displayTimeZone, artists, image }
    : null;
}

export function decodeSiteExhibition(body: unknown): SiteExhibition | null {
  const summary = decodeSiteExhibitionSummary(body);
  const f = fields(body);
  if (!summary || !f || !isNullableString(f['bodyText'])) return null;
  return { ...summary, bodyText: f['bodyText'] };
}

export function decodeSiteArtistSummary(body: unknown): SiteArtistSummary | null {
  const f = fields(body);
  if (!f) return null;
  const portrait = decodeNullable(f['portrait'], decodeSiteImage);
  const { id, slug, name, medium, active, workCount } = f;
  return isString(id) &&
    isString(slug) &&
    isString(name) &&
    isNullableString(medium) &&
    isBoolean(active) &&
    portrait !== undefined &&
    isNumber(workCount)
    ? { id, slug, name, medium, active, portrait, workCount }
    : null;
}

function decodeSiteArtist(body: unknown): SiteArtist | null {
  const f = fields(body);
  if (!f) return null;
  const portrait = decodeNullable(f['portrait'], decodeSiteImage);
  const { id, slug, name, medium, active, bioText } = f;
  return isString(id) &&
    isString(slug) &&
    isString(name) &&
    isNullableString(medium) &&
    isBoolean(active) &&
    portrait !== undefined &&
    isNullableString(bioText)
    ? { id, slug, name, medium, active, portrait, bioText }
    : null;
}

export function decodeSiteArtistPage(body: unknown): SiteArtistPage | null {
  const f = fields(body);
  const works = f && fields(f['works']);
  if (!f || !works || !isNullableString(works['nextCursor'])) return null;
  const artist = decodeSiteArtist(f['artist']);
  const items = decodeArray(works['items'], decodeSiteShopItemSummary);
  return artist && items ? { artist, works: { items, nextCursor: works['nextCursor'] } } : null;
}

export function decodeSiteShopItemSummary(body: unknown): SiteShopItemSummary | null {
  const f = fields(body);
  if (!f) return null;
  const artist = decodeNullable(f['artist'], decodeSiteCredit);
  const categories = decodeArray(f['categories'], (c) => (isString(c) ? c : null));
  const image = decodeNullable(f['image'], decodeSiteImage);
  const { id, slug, title, priceCents, currency, oneOfAKind, soldOut } = f;
  return isString(id) &&
    isString(slug) &&
    isString(title) &&
    artist !== undefined &&
    categories &&
    image !== undefined &&
    isNullableNumber(priceCents) &&
    isString(currency) &&
    isBoolean(oneOfAKind) &&
    isBoolean(soldOut)
    ? { id, slug, title, artist, categories, image, priceCents, currency, oneOfAKind, soldOut }
    : null;
}

export function decodeSiteShopItem(body: unknown): SiteShopItem | null {
  const summary = decodeSiteShopItemSummary(body);
  const f = fields(body);
  if (!summary || !f || !isNullableString(f['descriptionHtml'])) return null;
  const images = decodeArray(f['images'], decodeSiteImage);
  return images ? { ...summary, descriptionHtml: f['descriptionHtml'], images } : null;
}

export function decodeSiteShopPage(body: unknown): SiteShopPage | null {
  const f = fields(body);
  if (!f || !isNullableString(f['nextCursor']) || !isNumber(f['totalCount'])) return null;
  const items = decodeArray(f['items'], decodeSiteShopItemSummary);
  return items ? { items, nextCursor: f['nextCursor'], totalCount: f['totalCount'] } : null;
}

export function decodeSiteMembershipPlan(body: unknown): SiteMembershipPlan | null {
  const f = fields(body);
  if (!f) return null;
  const perks = decodeArray(f['perks'], (p) => (isString(p) ? p : null));
  const { id, slug, name, discountPercent, featured, priceCents, currency, cadence } = f;
  return isString(id) &&
    isString(slug) &&
    isString(name) &&
    perks &&
    isNumber(discountPercent) &&
    isBoolean(featured) &&
    isNullableNumber(priceCents) &&
    isString(currency) &&
    isNullableString(cadence)
    ? { id, slug, name, perks, discountPercent, featured, priceCents, currency, cadence }
    : null;
}

export function decodeSitePage(body: unknown): SitePage | null {
  const f = fields(body);
  if (!f) return null;
  const { id, slug, title, bodyHtml, updatedAt } = f;
  return isString(id) &&
    isString(slug) &&
    isString(title) &&
    isNullableString(bodyHtml) &&
    isString(updatedAt)
    ? { id, slug, title, bodyHtml, updatedAt }
    : null;
}

// ---- Previews (ADR 0036) ---------------------------------------------------------------------------------------

/** `PreviewPolicy.PathPrefixes`: the content types a preview link can name. */
export const SITE_PREVIEW_TYPES = [
  'artist',
  'instructor',
  'exhibition',
  'page',
  'class-offering',
  'shop-item',
  'membership-plan',
] as const;

export type SitePreviewType = (typeof SITE_PREVIEW_TYPES)[number];

/** `PublishStatus`, as the API spells it. */
export const SITE_PUBLISH_STATUSES = ['Draft', 'Scheduled', 'Published', 'Archived'] as const;

export type SitePublishStatus = (typeof SITE_PUBLISH_STATUSES)[number];

/**
 * `GET /site/preview/{token}`: one record as the site will show it, whatever its status (`ContentPreviewServiceModel`).
 * Exactly one member is set, and `entityType` says which.
 *
 * The records are the site's own models, the ones a published page renders, so a preview carries no staff
 * bookkeeping and nothing is dropped: every field here has a page that renders it (ADR 0064, 0065).
 */
export interface SitePreview {
  readonly entityType: SitePreviewType;
  readonly entityId: string;
  /** Where the record will live once published; `/memberships` for a tier, which has no address of its own. */
  readonly path: string;
  readonly status: SitePublishStatus;
  /** Always `true`: a preview is never indexed and never enters the sitemap. */
  readonly noIndex: boolean;
  readonly artist: SiteArtist | null;
  readonly instructor: SiteInstructor | null;
  readonly exhibition: SiteExhibition | null;
  readonly page: SitePage | null;
  readonly classOffering: SiteClass | null;
  readonly shopItem: SiteShopItem | null;
  readonly membershipPlan: SiteMembershipPlan | null;
}

export function decodeSitePreview(body: unknown): SitePreview | null {
  const f = fields(body);
  if (!f) return null;
  const { entityId, path, noIndex } = f;
  const entityType = SITE_PREVIEW_TYPES.find((t) => t === f['entityType']);
  const status = SITE_PUBLISH_STATUSES.find((s) => s === f['status']);
  if (!entityType || !status || !isString(entityId) || !isString(path) || !isBoolean(noIndex))
    return null;

  // Six of the seven members are null on any one answer, so a member left out of the JSON reads as absent; a
  // member that *is* there and does not decode fails the whole answer, as every other read's would.
  const member = <T>(name: string, decode: (body: unknown) => T | null) =>
    decodeNullable(f[name] ?? null, decode);
  const artist = member('artist', decodeSiteArtist);
  const instructor = member('instructor', decodeSiteInstructor);
  const exhibition = member('exhibition', decodeSiteExhibition);
  const page = member('page', decodeSitePage);
  const classOffering = member('classOffering', decodeSiteClass);
  const shopItem = member('shopItem', decodeSiteShopItem);
  const membershipPlan = member('membershipPlan', decodeSiteMembershipPlan);
  if (
    artist === undefined ||
    instructor === undefined ||
    exhibition === undefined ||
    page === undefined ||
    classOffering === undefined ||
    shopItem === undefined ||
    membershipPlan === undefined
  )
    return null;

  const preview: SitePreview = {
    entityType,
    entityId,
    path,
    status,
    noIndex,
    artist,
    instructor,
    exhibition,
    page,
    classOffering,
    shopItem,
    membershipPlan,
  };

  // The record the type names has to be there: an answer that says "artist" and carries none is not one we can show.
  return recordOf(preview) === null ? null : preview;
}

/** The one member a preview sets, chosen by its `entityType`. */
function recordOf(preview: SitePreview): object | null {
  switch (preview.entityType) {
    case 'artist':
      return preview.artist;
    case 'instructor':
      return preview.instructor;
    case 'exhibition':
      return preview.exhibition;
    case 'page':
      return preview.page;
    case 'class-offering':
      return preview.classOffering;
    case 'shop-item':
      return preview.shopItem;
    case 'membership-plan':
      return preview.membershipPlan;
  }
}
