/**
 * Exhibitions' API shapes, mirroring the API's ServiceModels exactly (`ExhibitionServiceModels.cs`, the OpenAPI
 * snapshot): string ids, `YYYY-MM-DD` dates, UTC ISO timestamps shown in `displayTimeZone`, enum names as strings.
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
export interface CreditedArtist {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  /** The artist's own status: a credit to an archived or draft artist is shown as such. */
  readonly status: PublishStatus;
}

export interface Exhibition {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
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
  readonly status: PublishStatus;
  /** When it was first published; kept through an unpublish. */
  readonly publishedAt: string | null;
  readonly scheduledFor: string | null;
  /** The slug can no longer change: the exhibition has been published. */
  readonly slugLocked: boolean;
  readonly legacyWordPressId: number | null;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly updatedBy: string;
  /** The row version, sent back as `If-Match`. */
  readonly etag: string;
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

export interface ExhibitionPage {
  readonly items: readonly ExhibitionSummary[];
  readonly nextCursor: string | null;
}

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
  readonly status?: PublishStatus;
  readonly search?: string;
}

const isStatus = oneOf(PUBLISH_STATUSES);
const isDate = (value: unknown): value is string =>
  isString(value) && /^\d{4}-\d{2}-\d{2}$/.test(value);

/** An int64: a number or a numeric string, as the OpenAPI schema allows. `undefined` when it is neither. */
function decodeInt64(value: unknown): number | null | undefined {
  if (value === null) return null;
  if (isNumber(value) && Number.isSafeInteger(value)) return value;
  if (typeof value === 'string' && /^-?(?:0|[1-9]\d*)$/.test(value)) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) ? parsed : undefined;
  }
  return undefined;
}

function decodeReception(body: unknown): ExhibitionReception | null | undefined {
  if (body === null) return null;
  const f = fields(body);
  if (!f || !isString(f['startsAt']) || !isString(f['endsAt']) || !isNullableString(f['note'])) {
    return undefined;
  }
  return { startsAt: f['startsAt'], endsAt: f['endsAt'], note: f['note'] };
}

function decodeCredit(body: unknown): CreditedArtist | null {
  const f = fields(body);
  if (
    !f ||
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['slug']) ||
    !isStatus(f['status'])
  ) {
    return null;
  }
  return { id: f['id'], name: f['name'], slug: f['slug'], status: f['status'] };
}

export function decodeExhibition(body: unknown): Exhibition | null {
  const f = fields(body);
  if (!f) return null;
  const legacyId = decodeInt64(f['legacyWordPressId']);
  const reception = decodeReception(f['reception']);
  const artists = Array.isArray(f['artists']) ? f['artists'].map(decodeCredit) : null;
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isNullableString(f['summary']) ||
    !isNullableString(f['body']) ||
    !isDate(f['startDate']) ||
    !isDate(f['endDate']) ||
    reception === undefined ||
    !isString(f['displayTimeZone']) ||
    !isNullableString(f['heroMediaId']) ||
    artists === null ||
    artists.some((artist) => artist === null) ||
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
    startDate: f['startDate'],
    endDate: f['endDate'],
    reception,
    displayTimeZone: f['displayTimeZone'],
    heroMediaId: f['heroMediaId'],
    artists: artists as CreditedArtist[],
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

export function decodeExhibitionSummary(body: unknown): ExhibitionSummary | null {
  const f = fields(body);
  if (!f) return null;
  // An int32: the OpenAPI schema allows a number or a numeric string, as for the int64 legacy id.
  const artistCount = decodeInt64(f['artistCount']);
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isDate(f['startDate']) ||
    !isDate(f['endDate']) ||
    !isStatus(f['status']) ||
    !isNullableString(f['heroMediaId']) ||
    artistCount === undefined ||
    artistCount === null ||
    !isString(f['updatedAt'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    title: f['title'],
    slug: f['slug'],
    startDate: f['startDate'],
    endDate: f['endDate'],
    status: f['status'],
    heroMediaId: f['heroMediaId'],
    artistCount,
    updatedAt: f['updatedAt'],
  };
}

export function decodeExhibitionPage(body: unknown): ExhibitionPage | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeExhibitionSummary);
  if (items.some((item) => item === null)) return null;
  return { items: items as ExhibitionSummary[], nextCursor: f['nextCursor'] };
}

export type ExhibitionProblem = ApiProblem;

export const decodeExhibitionProblem = decodeApiProblem;
