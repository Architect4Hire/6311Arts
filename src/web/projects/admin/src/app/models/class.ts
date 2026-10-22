/**
 * Classes' API shapes, mirroring the API's ServiceModels exactly (`ClassServiceModels.cs`, the OpenAPI snapshot):
 * string ids, UTC ISO instants shown in each run's `displayTimeZone`, integer cents with a currency, enum names
 * as strings.
 *
 * A class is offered in **runs** (ADR 0044): the same class over different terms, each with its own instructor,
 * sessions, seats, SKU and price. The run is what Square sells. What Square holds the record of — a run's
 * `priceCents`, `seatsRemaining` and `soldOut` — appears only on the read models here, never on a write one
 * (square.md, ADR 0032).
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
  cadenceMaxLength: 100,
  runLabelMaxLength: 100,
  runsMaxCount: 30,
  sessionsMaxCount: 60,
  capacityMaxSeats: 500,
  startingPriceMaxCents: 1_000_000,
  skuMaxLength: 64,
  searchMaxLength: 100,
} as const;

/** The problem codes the class screens act on: the shared content ones plus the catalog's SKU clash (ADR 0037). */
export const CLASS_CODES = {
  ...CONTENT_CODES,
  /** `CatalogErrorCodes.SkuTaken`: another class run or shop item already uses the SKU. */
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

/** The SKU the CMS suggests for a run (`SkuPolicy.ForClassRun`), shown before one is saved. */
export function suggestedSku(offeringNumber: number, runNumber: number): string {
  return `CLS-${offeringNumber}-${runNumber}`;
}

/** One sitting: when it meets. UTC, shown in its run's display zone. */
export interface ClassSession {
  readonly id: string;
  readonly startsAt: string;
  readonly endsAt: string;
}

/** The instructor a run names, as the Instructors module describes them now. */
export interface ClassInstructor {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly status: PublishStatus;
}

/** One run: who teaches it, when it meets, and what Square sells for it (ADR 0044). */
export interface ClassRun {
  readonly id: string;
  /** "Fall 2026", "Sunday mornings"; `null` when the class has one run and needs no name for it. */
  readonly label: string | null;
  /** `null` until someone is chosen: a run needs an instructor to be published. */
  readonly instructor: ClassInstructor | null;
  readonly cadence: string | null;
  readonly displayTimeZone: string;
  /** CMS-owned, unique across the CMS. */
  readonly sku: string;
  /** CMS-owned: how many seats it has. */
  readonly capacity: number;
  /** The CMS's draft price, the value the first Square push creates the variation with. */
  readonly startingPriceCents: number | null;
  readonly currency: string;
  /** `false` once Square has reported money or seats: from then on a price change is its own command (5.4a). */
  readonly startingPriceEditable: boolean;
  /** Square's record of the price; `null` until Square has reported one. */
  readonly priceCents: number | null;
  /** Square's record of the seats left; `null` until the first pull. */
  readonly seatsRemaining: number | null;
  readonly soldOut: boolean;
  /** Square has reported money or seats for this run: it can no longer be removed here (ADR 0037). */
  readonly squareHasRecord: boolean;
  /** The sittings, in start order. */
  readonly sessions: readonly ClassSession[];
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
  /** The number its runs' suggested SKUs are built from. */
  readonly offeringNumber: number;
  readonly heroMediaId: string | null;
  /** The library images the description shows. */
  readonly imageMediaIds: readonly string[];
  /** The runs, by their first session. */
  readonly runs: readonly ClassRun[];
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
  /** Who teaches its runs, by name, each once. */
  readonly instructors: readonly string[];
  readonly heroMediaId: string | null;
  /** How many runs it has, past ones included. */
  readonly runCount: number;
  /** When it next meets, of every run's sessions still to come; `null` when none are. */
  readonly nextSessionStartsAt: string | null;
  readonly updatedAt: string;
}

export interface ClassPage {
  readonly items: readonly ClassSummary[];
  readonly nextCursor: string | null;
}

/** A sitting as sent. */
export interface ClassSessionInput {
  readonly startsAt: string;
  readonly endsAt: string;
  /** The session being kept, on an edit; left out for a new one. */
  readonly id?: string;
}

/** A run as sent. No price, seats or sold-out: those are Square's record (ADR 0032). */
export interface ClassRunInput {
  readonly capacity: number;
  /** The run being kept, on an edit; left out for a new one. */
  readonly id?: string;
  readonly label?: string;
  readonly instructorId?: string;
  readonly cadence?: string;
  /** Left out for the next free suggestion. */
  readonly sku?: string;
  readonly displayTimeZone?: string;
  /** Left out once Square holds the run: the price is then Square's, and sending a different one is refused. */
  readonly startingPriceCents?: number;
  readonly sessions?: readonly ClassSessionInput[];
}

/** A `POST` body. A slug left out is made from the title. */
export interface ClassCreate {
  readonly title: string;
  readonly slug?: string;
  readonly summary?: string;
  readonly body?: string;
  readonly classType?: string;
  readonly level?: string;
  readonly heroMediaId?: string;
  readonly runs?: readonly ClassRunInput[];
}

/**
 * A `PATCH` body: a field left out is unchanged; an empty string clears a text field; `runs` replaces the runs, and
 * each run's `sessions` replaces its sittings.
 */
export interface ClassUpdate {
  readonly title?: string;
  readonly slug?: string;
  readonly summary?: string;
  readonly body?: string;
  readonly classType?: string;
  readonly level?: string;
  readonly heroMediaId?: string;
  readonly removeHero?: true;
  readonly runs?: readonly ClassRunInput[];
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
  if (!f || !isString(f['id']) || !isString(f['startsAt']) || !isString(f['endsAt'])) return null;
  return { id: f['id'], startsAt: f['startsAt'], endsAt: f['endsAt'] };
}

function decodeInstructor(body: unknown): ClassInstructor | null | undefined {
  if (body === null) return null;
  const f = fields(body);
  if (
    !f ||
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['slug']) ||
    !isStatus(f['status'])
  ) {
    return undefined;
  }
  return { id: f['id'], name: f['name'], slug: f['slug'], status: f['status'] };
}

function decodeRun(body: unknown): ClassRun | null {
  const f = fields(body);
  if (!f) return null;
  const capacity = decodeInteger(f['capacity']);
  const startingPriceCents = decodeInteger(f['startingPriceCents']);
  const priceCents = decodeInteger(f['priceCents']);
  const seatsRemaining = decodeInteger(f['seatsRemaining']);
  const instructor = decodeInstructor(f['instructor']);
  const sessions = Array.isArray(f['sessions']) ? f['sessions'].map(decodeSession) : null;
  if (
    !isString(f['id']) ||
    !isNullableString(f['label']) ||
    instructor === undefined ||
    !isNullableString(f['cadence']) ||
    !isString(f['displayTimeZone']) ||
    !isString(f['sku']) ||
    capacity === undefined ||
    capacity === null ||
    startingPriceCents === undefined ||
    !isString(f['currency']) ||
    !isBoolean(f['startingPriceEditable']) ||
    priceCents === undefined ||
    seatsRemaining === undefined ||
    !isBoolean(f['soldOut']) ||
    !isBoolean(f['squareHasRecord']) ||
    sessions === null ||
    sessions.some((session) => session === null)
  ) {
    return null;
  }

  return {
    id: f['id'],
    label: f['label'],
    instructor,
    cadence: f['cadence'],
    displayTimeZone: f['displayTimeZone'],
    sku: f['sku'],
    capacity,
    startingPriceCents,
    currency: f['currency'],
    startingPriceEditable: f['startingPriceEditable'],
    priceCents,
    seatsRemaining,
    soldOut: f['soldOut'],
    squareHasRecord: f['squareHasRecord'],
    sessions: sessions as ClassSession[],
  };
}

export function decodeClassOffering(body: unknown): ClassOffering | null {
  const f = fields(body);
  if (!f) return null;
  const legacyId = decodeInteger(f['legacyWordPressId']);
  const offeringNumber = decodeInteger(f['offeringNumber']);
  const images = Array.isArray(f['imageMediaIds']) ? f['imageMediaIds'] : null;
  const runs = Array.isArray(f['runs']) ? f['runs'].map(decodeRun) : null;
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isNullableString(f['summary']) ||
    !isNullableString(f['body']) ||
    !isNullableString(f['classType']) ||
    !isNullableString(f['level']) ||
    offeringNumber === undefined ||
    offeringNumber === null ||
    !isNullableString(f['heroMediaId']) ||
    images === null ||
    !images.every(isString) ||
    runs === null ||
    runs.some((run) => run === null) ||
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
    offeringNumber,
    heroMediaId: f['heroMediaId'],
    imageMediaIds: images,
    runs: runs as ClassRun[],
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
  const runCount = decodeInteger(f['runCount']);
  const instructors = Array.isArray(f['instructors']) ? f['instructors'] : null;
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isStatus(f['status']) ||
    !isNullableString(f['classType']) ||
    !isNullableString(f['level']) ||
    instructors === null ||
    !instructors.every(isString) ||
    !isNullableString(f['heroMediaId']) ||
    runCount === undefined ||
    runCount === null ||
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
    instructors,
    heroMediaId: f['heroMediaId'],
    runCount,
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

/** One row of the Deleted view (`ClassDeletedServiceModel`, ADR 0052). Admin only. */
export interface ClassDeletedRow {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  /** What it was when it was deleted, not where it is now. */
  readonly status: PublishStatus;
  readonly deletedAt: string;
  /** The row version, sent back as `If-Match` when restoring it. */
  readonly etag: string;
}

export interface ClassDeletedList {
  readonly items: readonly ClassDeletedRow[];
  readonly nextCursor: string | null;
}

export function decodeClassDeletedRow(body: unknown): ClassDeletedRow | null {
  const f = fields(body);
  if (
    !f ||
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isStatus(f['status']) ||
    !isString(f['deletedAt']) ||
    !isString(f['etag'])
  ) {
    return null;
  }
  return {
    id: f['id'],
    title: f['title'],
    slug: f['slug'],
    status: f['status'],
    deletedAt: f['deletedAt'],
    etag: f['etag'],
  };
}

export function decodeClassDeletedList(body: unknown): ClassDeletedList | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeClassDeletedRow);
  if (items.some((item) => item === null)) return null;
  return { items: items as ClassDeletedRow[], nextCursor: f['nextCursor'] };
}
