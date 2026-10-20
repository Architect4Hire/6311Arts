/**
 * Classes' API shapes, mirroring the API's ServiceModels exactly (`ClassServiceModels.cs`, the OpenAPI snapshot):
 * string ids, UTC ISO instants shown in each session's `displayTimeZone`, integer cents with a currency, enum names
 * as strings.
 *
 * What Square holds the record of — a session's `priceCents`, `seatsRemaining` and `soldOut` — appears only on the
 * read models here, never on a write one (square.md, ADR 0032).
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

/** The limits the API enforces (`ClassPolicy`, `SkuPolicy`, `Slugs`), so the form can stop early and say why. */
export const CLASS_LIMITS = {
  titleMaxLength: 200,
  slugMaxLength: SLUG_MAX_LENGTH,
  summaryMaxLength: 500,
  classTypeMaxLength: 60,
  levelMaxLength: 60,
  instructorMaxLength: 200,
  cadenceMaxLength: 100,
  sessionsMaxCount: 60,
  capacityMaxSeats: 500,
  startingPriceMaxCents: 1_000_000,
  skuMaxLength: 64,
  searchMaxLength: 100,
} as const;

/** The problem codes the class screens act on: the shared content ones plus the catalog's SKU clash (ADR 0037). */
export const CLASS_CODES = {
  ...CONTENT_CODES,
  /** `CatalogErrorCodes.SkuTaken`: another class session or shop item already uses the SKU. */
  skuTaken: 'cms.sku_taken',
} as const;

export type ClassCommand = ContentCommand;

/** The shape the API accepts as a SKU (`SkuPolicy.IsValid`), once normalized. */
export function isSku(value: string): boolean {
  return (
    value.length > 0 &&
    value.length <= CLASS_LIMITS.skuMaxLength &&
    /^[A-Z0-9]+(?:[-.][A-Z0-9]+)*$/.test(value)
  );
}

/** A SKU as the API stores and compares it (`SkuPolicy.Normalize`): trimmed and upper-cased. */
export function normalizeSku(value: string): string {
  return value.trim().toUpperCase();
}

/** The SKU the CMS suggests for a session (`SkuPolicy.ForClassSession`), shown before one is saved. */
export function suggestedSku(offeringNumber: number, sessionNumber: number): string {
  return `CLS-${offeringNumber}-${sessionNumber}`;
}

/** One sitting. Its times are UTC; `displayTimeZone` says which clock they are shown on. */
export interface ClassSession {
  readonly id: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly displayTimeZone: string;
  /** CMS-owned, unique across the CMS. */
  readonly sku: string;
  /** CMS-owned: how many seats it has. */
  readonly capacity: number;
  /** Square's record of the price; `null` until Square has reported one. */
  readonly priceCents: number | null;
  /** Square's record of the seats left; `null` until the first pull. */
  readonly seatsRemaining: number | null;
  readonly soldOut: boolean;
  /** Square has reported money or seats for this session: it can no longer be removed here (ADR 0037). */
  readonly squareHasRecord: boolean;
}

export interface ClassOffering {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly summary: string | null;
  /** Sanitized HTML, exactly as stored. */
  readonly body: string | null;
  readonly classType: string | null;
  readonly level: string | null;
  readonly instructor: string | null;
  readonly cadence: string | null;
  /** The CMS's draft price, the value the first Square push creates the variations with. */
  readonly startingPriceCents: number | null;
  readonly currency: string;
  /** `false` once Square has reported a price: from then on a change is its own command (5.4a). */
  readonly startingPriceEditable: boolean;
  /** The number its suggested SKUs are built from. */
  readonly offeringNumber: number;
  readonly heroMediaId: string | null;
  /** The library images the description shows. */
  readonly imageMediaIds: readonly string[];
  /** The sittings, in start order. */
  readonly sessions: readonly ClassSession[];
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

export interface ClassSummary {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly status: PublishStatus;
  readonly classType: string | null;
  readonly level: string | null;
  readonly instructor: string | null;
  readonly heroMediaId: string | null;
  readonly sessionCount: number;
  /** When it next meets, of the sessions still to come; `null` when none are. */
  readonly nextSessionStartsAt: string | null;
  readonly updatedAt: string;
}

export interface ClassPage {
  readonly items: readonly ClassSummary[];
  readonly nextCursor: string | null;
}

/** A sitting as sent. No price, seats or sold-out: those are Square's record (ADR 0032). */
export interface ClassSessionInput {
  readonly startsAt: string;
  readonly endsAt: string;
  readonly capacity: number;
  /** The session being kept, on an edit; left out for a new one. */
  readonly id?: string;
  /** Left out for the next free suggestion. */
  readonly sku?: string;
  readonly displayTimeZone?: string;
}

/** A `POST` body. A slug left out is made from the title. */
export interface ClassCreate {
  readonly title: string;
  readonly slug?: string;
  readonly summary?: string;
  readonly body?: string;
  readonly classType?: string;
  readonly level?: string;
  readonly instructor?: string;
  readonly cadence?: string;
  readonly startingPriceCents?: number;
  readonly heroMediaId?: string;
  readonly sessions?: readonly ClassSessionInput[];
}

/**
 * A `PATCH` body: a field left out is unchanged; an empty string clears a text field; `sessions` replaces the
 * sittings.
 */
export interface ClassUpdate {
  readonly title?: string;
  readonly slug?: string;
  readonly summary?: string;
  readonly body?: string;
  readonly classType?: string;
  readonly level?: string;
  readonly instructor?: string;
  readonly cadence?: string;
  readonly startingPriceCents?: number;
  readonly removeStartingPrice?: true;
  readonly heroMediaId?: string;
  readonly removeHero?: true;
  readonly sessions?: readonly ClassSessionInput[];
}

export interface ClassListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly status?: PublishStatus;
  readonly search?: string;
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

function decodeSession(body: unknown): ClassSession | null {
  const f = fields(body);
  if (!f) return null;
  const capacity = decodeInteger(f['capacity']);
  const priceCents = decodeInteger(f['priceCents']);
  const seatsRemaining = decodeInteger(f['seatsRemaining']);
  if (
    !isString(f['id']) ||
    !isString(f['startsAt']) ||
    !isString(f['endsAt']) ||
    !isString(f['displayTimeZone']) ||
    !isString(f['sku']) ||
    capacity === undefined ||
    capacity === null ||
    priceCents === undefined ||
    seatsRemaining === undefined ||
    !isBoolean(f['soldOut']) ||
    !isBoolean(f['squareHasRecord'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    startsAt: f['startsAt'],
    endsAt: f['endsAt'],
    displayTimeZone: f['displayTimeZone'],
    sku: f['sku'],
    capacity,
    priceCents,
    seatsRemaining,
    soldOut: f['soldOut'],
    squareHasRecord: f['squareHasRecord'],
  };
}

export function decodeClassOffering(body: unknown): ClassOffering | null {
  const f = fields(body);
  if (!f) return null;
  const legacyId = decodeInteger(f['legacyWordPressId']);
  const startingPriceCents = decodeInteger(f['startingPriceCents']);
  const offeringNumber = decodeInteger(f['offeringNumber']);
  const images = Array.isArray(f['imageMediaIds']) ? f['imageMediaIds'] : null;
  const sessions = Array.isArray(f['sessions']) ? f['sessions'].map(decodeSession) : null;
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isNullableString(f['summary']) ||
    !isNullableString(f['body']) ||
    !isNullableString(f['classType']) ||
    !isNullableString(f['level']) ||
    !isNullableString(f['instructor']) ||
    !isNullableString(f['cadence']) ||
    startingPriceCents === undefined ||
    !isString(f['currency']) ||
    !isBoolean(f['startingPriceEditable']) ||
    offeringNumber === undefined ||
    offeringNumber === null ||
    !isNullableString(f['heroMediaId']) ||
    images === null ||
    !images.every(isString) ||
    sessions === null ||
    sessions.some((session) => session === null) ||
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
    summary: f['summary'],
    body: f['body'],
    classType: f['classType'],
    level: f['level'],
    instructor: f['instructor'],
    cadence: f['cadence'],
    startingPriceCents,
    currency: f['currency'],
    startingPriceEditable: f['startingPriceEditable'],
    offeringNumber,
    heroMediaId: f['heroMediaId'],
    imageMediaIds: images,
    sessions: sessions as ClassSession[],
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

export function decodeClassSummary(body: unknown): ClassSummary | null {
  const f = fields(body);
  if (!f) return null;
  const sessionCount = decodeInteger(f['sessionCount']);
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isStatus(f['status']) ||
    !isNullableString(f['classType']) ||
    !isNullableString(f['level']) ||
    !isNullableString(f['instructor']) ||
    !isNullableString(f['heroMediaId']) ||
    sessionCount === undefined ||
    sessionCount === null ||
    !isNullableString(f['nextSessionStartsAt']) ||
    !isString(f['updatedAt'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    title: f['title'],
    slug: f['slug'],
    status: f['status'],
    classType: f['classType'],
    level: f['level'],
    instructor: f['instructor'],
    heroMediaId: f['heroMediaId'],
    sessionCount,
    nextSessionStartsAt: f['nextSessionStartsAt'],
    updatedAt: f['updatedAt'],
  };
}

export function decodeClassPage(body: unknown): ClassPage | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeClassSummary);
  if (items.some((item) => item === null)) return null;
  return { items: items as ClassSummary[], nextCursor: f['nextCursor'] };
}

export type ClassProblem = ApiProblem;

export const decodeClassProblem = decodeApiProblem;
