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
  CONTENT_FIELDS,
  ContentCommand,
  ContentRecord,
  Credit,
  credit,
  decodeDeletedList,
  DeletedList,
  DeletedRow,
  PublishStatus,
  SLUG_MAX_LENGTH,
  status,
} from './content';
import {
  ApiProblem,
  CursorPage,
  decodeApiProblem,
  decodeCursorPage,
  decodeShape,
  flag,
  int64,
  listOf,
  nullable,
  optionalText,
  shaped,
  text,
  whole,
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
export type ClassInstructor = Credit;

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

export interface ClassOffering extends ContentRecord {
  readonly title: string;
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
  /** It has been published, so changing its web address redirects the old one (ADR 0035). */
  readonly slugLocked: boolean;
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

export type ClassPage = CursorPage<ClassSummary>;

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
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly status?: PublishStatus;
  readonly search?: string;
}

/** One term of a class (ADR 0044): its instructor, sessions, seats and price. */
const run = shaped({
  id: text,
  label: optionalText,
  instructor: nullable(credit),
  cadence: optionalText,
  displayTimeZone: text,
  sku: text,
  capacity: whole,
  startingPriceCents: int64,
  currency: text,
  startingPriceEditable: flag,
  priceCents: int64,
  seatsRemaining: int64,
  soldOut: flag,
  squareHasRecord: flag,
  sessions: listOf(shaped({ id: text, startsAt: text, endsAt: text })),
});

export function decodeClassOffering(body: unknown): ClassOffering | null {
  return decodeShape(body, {
    ...CONTENT_FIELDS,
    title: text,
    summary: optionalText,
    body: optionalText,
    classType: optionalText,
    level: optionalText,
    offeringNumber: whole,
    heroMediaId: optionalText,
    imageMediaIds: listOf(text),
    runs: listOf(run),
    slugLocked: flag,
  });
}

export function decodeClassSummary(body: unknown): ClassSummary | null {
  return decodeShape(body, {
    id: text,
    title: text,
    slug: text,
    status,
    classType: optionalText,
    level: optionalText,
    instructors: listOf(text),
    heroMediaId: optionalText,
    runCount: whole,
    nextSessionStartsAt: optionalText,
    updatedAt: text,
  });
}

export function decodeClassPage(body: unknown): ClassPage | null {
  return decodeCursorPage(body, decodeClassSummary);
}

export type ClassProblem = ApiProblem;

export const decodeClassProblem = decodeApiProblem;

/** One row of the Deleted view (`ClassDeletedServiceModel`, ADR 0052). Admin only. */
export type ClassDeletedRow = DeletedRow<'title'>;

export type ClassDeletedList = DeletedList<'title'>;

export function decodeClassDeletedList(body: unknown): ClassDeletedList | null {
  return decodeDeletedList(body, 'title');
}
