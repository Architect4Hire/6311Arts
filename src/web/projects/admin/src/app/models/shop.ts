/**
 * The shop's API shapes, mirroring the API's ServiceModels exactly (`ShopItemServiceModels.cs`, the OpenAPI
 * snapshot): string ids, UTC ISO instants, integer cents with a currency, enum names as strings.
 *
 * What Square holds the record of — `priceCents`, `stockOnHand` and `soldOut` — appears only on the read models
 * here, never on a write one (square.md, ADR 0032).
 */

import {
  CONTENT_CODES,
  ContentCommand,
  PUBLISH_STATUSES,
  PublishStatus,
  SLUG_MAX_LENGTH,
} from './content';
import {
  ApiProblem,
  decodeApiProblem,
  fields,
  isBoolean,
  isNullableString,
  isNumber,
  isString,
  oneOf,
} from './decode';

/** The limits the API enforces (`ShopPolicy`, `SkuPolicy`, `Slugs`), so the form can stop early and say why. */
export const SHOP_LIMITS = {
  titleMaxLength: 200,
  descriptionMaxLength: 100_000,
  descriptionInputMaxLength: 200_000,
  slugMaxLength: SLUG_MAX_LENGTH,
  galleryMaxCount: 12,
  startingPriceMaxCents: 5_000_000,
  startingStockMaxQuantity: 1_000,
  skuMaxLength: 64,
  searchMaxLength: 100,
} as const;

/**
 * The categories artwork is filed under, mirroring `ShopPolicy.Categories` (ADR 0038): the slugs are the contract
 * and the labels are ours to show, as `SHOP_LIMITS` mirrors the policy's numbers. The site keeps its own copy.
 */
export const SHOP_CATEGORIES = [
  { slug: 'jewelry', label: 'Jewelry' },
  { slug: 'prints', label: 'Prints' },
  { slug: 'ceramics', label: 'Ceramics' },
  { slug: 'painting', label: 'Painting' },
  { slug: 'sculpture', label: 'Sculpture' },
  { slug: 'books-magazines', label: 'Books and magazines' },
] as const;

export type ShopCategory = (typeof SHOP_CATEGORIES)[number]['slug'];

/** The label for a category slug; the slug itself for anything the API sends that this list does not know. */
export function categoryLabel(slug: string): string {
  return SHOP_CATEGORIES.find((category) => category.slug === slug)?.label ?? slug;
}

/** The problem codes the shop screens act on: the shared content ones plus the catalog's SKU clash (ADR 0037). */
export const SHOP_CODES = {
  ...CONTENT_CODES,
  /** `CatalogErrorCodes.SkuTaken`: another shop item or class session already uses the SKU. */
  skuTaken: 'cms.sku_taken',
} as const;

export type ShopCommand = ContentCommand;

/** The shape the API accepts as a SKU (`SkuPolicy.IsValid`), once normalized. */
export function isSku(value: string): boolean {
  return (
    value.length > 0 &&
    value.length <= SHOP_LIMITS.skuMaxLength &&
    /^[A-Z0-9]+(?:[-.][A-Z0-9]+)*$/.test(value)
  );
}

/** A SKU as the API stores and compares it (`SkuPolicy.Normalize`): trimmed and upper-cased. */
export function normalizeSku(value: string): string {
  return value.trim().toUpperCase();
}

/** The artist a piece credits, as the Artists module describes them now. */
export interface ShopItemArtist {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  /** The artist's own status: work credited to an archived or draft artist is shown as such. */
  readonly status: PublishStatus;
}

export interface ShopItem {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  /** Sanitized HTML, exactly as stored. */
  readonly description: string | null;
  readonly artist: ShopItemArtist | null;
  readonly categories: readonly string[];
  /** The photographs, in order. The first is the lead (ADR 0038). */
  readonly imageMediaIds: readonly string[];
  /** The library images the description shows. */
  readonly descriptionImageMediaIds: readonly string[];
  readonly sku: string;
  /** The number its suggested SKU is built from. */
  readonly itemNumber: number;
  readonly oneOfAKind: boolean;
  readonly startingPriceCents: number | null;
  readonly startingStockQuantity: number | null;
  readonly currency: string;
  /** `false` once Square has reported money or stock: from then on a change is its own command (5.4a). */
  readonly startingPriceEditable: boolean;
  /** The same gate for the one-of-a-kind flag and the starting count (5.4b, ADR 0038). */
  readonly startingStockEditable: boolean;
  /** Square's record of the price; `null` until Square has reported one. */
  readonly priceCents: number | null;
  /** Square's record of the stock; `null` until the first pull. */
  readonly stockOnHand: number | null;
  readonly soldOut: boolean;
  /** Square has reported money or stock for this piece (ADR 0037, 0038). */
  readonly squareHasRecord: boolean;
  readonly status: PublishStatus;
  readonly publishedAt: string | null;
  readonly scheduledFor: string | null;
  /** It has been published, so changing its web address redirects the old one (ADR 0035). */
  readonly slugLocked: boolean;
  readonly legacyWordPressId: number | null;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly updatedBy: string;
  /** The row version, sent back as `If-Match`. */
  readonly etag: string;
}

export interface ShopItemSummary {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly status: PublishStatus;
  readonly artist: ShopItemArtist | null;
  readonly categories: readonly string[];
  readonly leadImageMediaId: string | null;
  readonly sku: string;
  readonly oneOfAKind: boolean;
  readonly priceCents: number | null;
  readonly stockOnHand: number | null;
  readonly soldOut: boolean;
  /** The currency `priceCents` is in (api-contract.md). */
  readonly currency: string;
  readonly updatedAt: string;
}

export interface ShopPage {
  readonly items: readonly ShopItemSummary[];
  readonly nextCursor: string | null;
}

/** A `POST` body. A slug left out is made from the title, and a SKU left out is suggested as `ART-<item>`. */
export interface ShopItemCreate {
  readonly title: string;
  readonly slug?: string;
  readonly description?: string;
  readonly artistId?: string;
  readonly categories?: readonly string[];
  readonly imageMediaIds?: readonly string[];
  readonly sku?: string;
  readonly oneOfAKind?: boolean;
  readonly startingPriceCents?: number;
  readonly startingStockQuantity?: number;
}

/**
 * A `PATCH` body: a field left out is unchanged; an empty string clears the description; `categories` and
 * `imageMediaIds` replace their sets.
 */
export interface ShopItemUpdate {
  readonly title?: string;
  readonly slug?: string;
  readonly description?: string;
  readonly artistId?: string;
  readonly removeArtist?: true;
  readonly categories?: readonly string[];
  readonly imageMediaIds?: readonly string[];
  readonly sku?: string;
  readonly oneOfAKind?: boolean;
  readonly startingPriceCents?: number;
  readonly removeStartingPrice?: true;
  readonly startingStockQuantity?: number;
}

export interface ShopListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly status?: PublishStatus;
  readonly search?: string;
  readonly category?: string;
}

const isStatus = oneOf(PUBLISH_STATUSES);

/** An integer the OpenAPI schema allows as a number or a numeric string. `undefined` when it is neither. */
function decodeInteger(value: unknown): number | null | undefined {
  if (value === null) return null;
  if (isNumber(value) && Number.isSafeInteger(value)) return value;
  if (typeof value === 'string' && /^-?(?:0|[1-9]\d*)$/.test(value)) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) ? parsed : undefined;
  }
  return undefined;
}

function decodeArtist(body: unknown): ShopItemArtist | null | undefined {
  if (body === null) return null;
  const f = fields(body);
  if (!f || !isString(f['id']) || !isString(f['name']) || !isString(f['slug']) || !isStatus(f['status'])) {
    return undefined;
  }
  return { id: f['id'], name: f['name'], slug: f['slug'], status: f['status'] };
}

export function decodeShopItem(body: unknown): ShopItem | null {
  const f = fields(body);
  if (!f) return null;
  const artist = decodeArtist(f['artist']);
  const categories = Array.isArray(f['categories']) ? f['categories'] : null;
  const gallery = Array.isArray(f['imageMediaIds']) ? f['imageMediaIds'] : null;
  const descriptionImages = Array.isArray(f['descriptionImageMediaIds'])
    ? f['descriptionImageMediaIds']
    : null;
  const itemNumber = decodeInteger(f['itemNumber']);
  const startingPriceCents = decodeInteger(f['startingPriceCents']);
  const startingStockQuantity = decodeInteger(f['startingStockQuantity']);
  const priceCents = decodeInteger(f['priceCents']);
  const stockOnHand = decodeInteger(f['stockOnHand']);
  const legacyId = decodeInteger(f['legacyWordPressId']);
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isNullableString(f['description']) ||
    artist === undefined ||
    categories === null ||
    !categories.every(isString) ||
    gallery === null ||
    !gallery.every(isString) ||
    descriptionImages === null ||
    !descriptionImages.every(isString) ||
    !isString(f['sku']) ||
    itemNumber === undefined ||
    itemNumber === null ||
    !isBoolean(f['oneOfAKind']) ||
    startingPriceCents === undefined ||
    startingStockQuantity === undefined ||
    !isString(f['currency']) ||
    !isBoolean(f['startingPriceEditable']) ||
    !isBoolean(f['startingStockEditable']) ||
    priceCents === undefined ||
    stockOnHand === undefined ||
    !isBoolean(f['soldOut']) ||
    !isBoolean(f['squareHasRecord']) ||
    !isStatus(f['status']) ||
    !isNullableString(f['publishedAt']) ||
    !isNullableString(f['scheduledFor']) ||
    !isBoolean(f['slugLocked']) ||
    legacyId === undefined ||
    !isString(f['createdAt']) ||
    !isString(f['updatedAt']) ||
    !isString(f['updatedBy']) ||
    !isString(f['etag'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    title: f['title'],
    slug: f['slug'],
    description: f['description'],
    artist,
    categories,
    imageMediaIds: gallery,
    descriptionImageMediaIds: descriptionImages,
    sku: f['sku'],
    itemNumber,
    oneOfAKind: f['oneOfAKind'],
    startingPriceCents,
    startingStockQuantity,
    currency: f['currency'],
    startingPriceEditable: f['startingPriceEditable'],
    startingStockEditable: f['startingStockEditable'],
    priceCents,
    stockOnHand,
    soldOut: f['soldOut'],
    squareHasRecord: f['squareHasRecord'],
    status: f['status'],
    publishedAt: f['publishedAt'],
    scheduledFor: f['scheduledFor'],
    slugLocked: f['slugLocked'],
    legacyWordPressId: legacyId,
    createdAt: f['createdAt'],
    updatedAt: f['updatedAt'],
    updatedBy: f['updatedBy'],
    etag: f['etag'],
  };
}

export function decodeShopItemSummary(body: unknown): ShopItemSummary | null {
  const f = fields(body);
  if (!f) return null;
  const artist = decodeArtist(f['artist']);
  const categories = Array.isArray(f['categories']) ? f['categories'] : null;
  const priceCents = decodeInteger(f['priceCents']);
  const stockOnHand = decodeInteger(f['stockOnHand']);
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isStatus(f['status']) ||
    artist === undefined ||
    categories === null ||
    !categories.every(isString) ||
    !isNullableString(f['leadImageMediaId']) ||
    !isString(f['sku']) ||
    !isBoolean(f['oneOfAKind']) ||
    priceCents === undefined ||
    stockOnHand === undefined ||
    !isBoolean(f['soldOut']) ||
    !isString(f['currency']) ||
    !isString(f['updatedAt'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    title: f['title'],
    slug: f['slug'],
    status: f['status'],
    artist,
    categories,
    leadImageMediaId: f['leadImageMediaId'],
    sku: f['sku'],
    oneOfAKind: f['oneOfAKind'],
    priceCents,
    stockOnHand,
    soldOut: f['soldOut'],
    currency: f['currency'],
    updatedAt: f['updatedAt'],
  };
}

export function decodeShopPage(body: unknown): ShopPage | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeShopItemSummary);
  if (items.some((item) => item === null)) return null;
  return { items: items as ShopItemSummary[], nextCursor: f['nextCursor'] };
}

export type ShopProblem = ApiProblem;

export const decodeShopProblem = decodeApiProblem;
