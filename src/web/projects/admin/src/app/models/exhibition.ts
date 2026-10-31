/**
 * Exhibitions' API shapes, mirroring the API's ServiceModels exactly (`ExhibitionServiceModels.cs`, the OpenAPI
 * snapshot): string ids, `YYYY-MM-DD` dates, UTC ISO timestamps shown in `displayTimeZone`, enum names as strings.
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
  field,
  flag,
  isString,
  listOf,
  nullable,
  optionalText,
  shaped,
  text,
  whole,
} from './decode';

/** The limits the API enforces (`ExhibitionPolicy`, `Slugs`), so the form can stop early and say why. */
export const EXHIBITION_LIMITS = {
  titleMaxLength: 200,
  slugMaxLength: SLUG_MAX_LENGTH,
  summaryMaxLength: 500,
  bodyMaxLength: 10_000,
  receptionNoteMaxLength: 200,
  artistsMaxCount: 50,
  searchMaxLength: 100,
} as const;

/** The problem codes the exhibition screens act on: the ones every content type shares. */
export const EXHIBITION_CODES = CONTENT_CODES;

export type ExhibitionCommand = ContentCommand;

/** An opening reception: both instants UTC, shown in the exhibition's `displayTimeZone`. */
export interface ExhibitionReception {
  readonly startsAt: string;
  readonly endsAt: string;
  readonly note: string | null;
}

/** A credited artist, as the Artists module describes them now. */
export type CreditedArtist = Credit;

export interface Exhibition extends ContentRecord {
  readonly title: string;
  readonly summary: string | null;
  readonly body: string | null;
  /** `YYYY-MM-DD`: the first day it hangs. */
  readonly startDate: string;
  /** `YYYY-MM-DD`: the last day it hangs, inclusive. */
  readonly endDate: string;
  readonly reception: ExhibitionReception | null;
  /** The IANA zone the reception's times are shown in. */
  readonly displayTimeZone: string;
  readonly heroMediaId: string | null;
  /** The credits, in order. */
  readonly artists: readonly CreditedArtist[];
  /** The slug can no longer change: the exhibition has been published. */
  readonly slugLocked: boolean;
}

export interface ExhibitionSummary {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly status: PublishStatus;
  readonly heroMediaId: string | null;
  readonly artistCount: number;
  readonly updatedAt: string;
}

export type ExhibitionPage = CursorPage<ExhibitionSummary>;

/** A reception as sent: both instants, any offset (the API keeps them as UTC). */
export interface ExhibitionReceptionInput {
  readonly startsAt: string;
  readonly endsAt: string;
  readonly note?: string;
}

/** A `POST` body. A slug left out is made from the title. */
export interface ExhibitionCreate {
  readonly title: string;
  readonly startDate: string;
  readonly endDate: string;
  readonly slug?: string;
  readonly summary?: string;
  readonly body?: string;
  readonly reception?: ExhibitionReceptionInput;
  readonly heroMediaId?: string;
  readonly artistIds?: readonly string[];
}

/**
 * A `PATCH` body: a field left out is unchanged; an empty string clears `summary` or `body`; `artistIds` replaces the
 * credits in order.
 */
export interface ExhibitionUpdate {
  readonly title?: string;
  readonly slug?: string;
  readonly summary?: string;
  readonly body?: string;
  readonly startDate?: string;
  readonly endDate?: string;
  readonly reception?: ExhibitionReceptionInput;
  readonly removeReception?: true;
  readonly heroMediaId?: string;
  readonly removeHero?: true;
  readonly artistIds?: readonly string[];
}

export interface ExhibitionListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly status?: PublishStatus;
  readonly search?: string;
}

const isDate = (value: unknown): value is string =>
  isString(value) && /^\d{4}-\d{2}-\d{2}$/.test(value);

const date = field(isDate);

export function decodeExhibition(body: unknown): Exhibition | null {
  return decodeShape(body, {
    ...CONTENT_FIELDS,
    title: text,
    summary: optionalText,
    body: optionalText,
    startDate: date,
    endDate: date,
    reception: nullable(shaped({ startsAt: text, endsAt: text, note: optionalText })),
    displayTimeZone: text,
    heroMediaId: optionalText,
    artists: listOf(credit),
    slugLocked: flag,
  });
}

export function decodeExhibitionSummary(body: unknown): ExhibitionSummary | null {
  return decodeShape(body, {
    id: text,
    title: text,
    slug: text,
    startDate: date,
    endDate: date,
    status,
    heroMediaId: optionalText,
    artistCount: whole,
    updatedAt: text,
  });
}

export function decodeExhibitionPage(body: unknown): ExhibitionPage | null {
  return decodeCursorPage(body, decodeExhibitionSummary);
}

export type ExhibitionProblem = ApiProblem;

export const decodeExhibitionProblem = decodeApiProblem;

/** One row of the Deleted view (`ExhibitionDeletedServiceModel`, ADR 0052). Admin only. */
export type ExhibitionDeletedRow = DeletedRow<'title'>;

export type ExhibitionDeletedList = DeletedList<'title'>;

export function decodeExhibitionDeletedList(body: unknown): ExhibitionDeletedList | null {
  return decodeDeletedList(body, 'title');
}
