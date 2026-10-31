/**
 * The artist roster's API shapes, mirroring the API's ServiceModels exactly (`ArtistServiceModels.cs`, the OpenAPI
 * snapshot): string ids, ISO timestamps, enum names as strings.
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

// The lifecycle, its chips and the slug rules are shared by every content type (`content.ts`).
export {
  PUBLISH_STATUSES,
  type PublishStatus,
  STATUS_CHIPS,
  isSlug,
  slugFromName,
} from './content';

/** The limits the API enforces (`ArtistPolicy`, `Slugs`), so the form can stop early and say why. */
export const ARTIST_LIMITS = {
  nameMaxLength: 200,
  slugMaxLength: SLUG_MAX_LENGTH,
  mediumMaxLength: 100,
  bioMaxLength: 5000,
  searchMaxLength: 100,
} as const;

/** The problem codes the artist screens act on: the ones every content type shares. */
export const ARTIST_CODES = CONTENT_CODES;

export interface Artist {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly medium: string | null;
  readonly bio: string | null;
  readonly active: boolean;
  readonly portraitMediaId: string | null;
  readonly status: PublishStatus;
  /** When it was first published; kept through an unpublish. */
  readonly publishedAt: string | null;
  readonly scheduledFor: string | null;
  /** The slug can no longer change: the artist has been published. */
  readonly slugLocked: boolean;
  readonly legacyWordPressId: number | null;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly updatedBy: string;
  /** The row version, sent back as `If-Match`. */
  readonly etag: string;
}

export interface ArtistSummary {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly medium: string | null;
  readonly active: boolean;
  readonly status: PublishStatus;
  readonly portraitMediaId: string | null;
  readonly updatedAt: string;
}

export interface ArtistPage {
  readonly items: readonly ArtistSummary[];
  readonly nextCursor: string | null;
}

/** A `POST` body. A slug left out is made from the name. */
export interface ArtistCreate {
  readonly name: string;
  readonly slug?: string;
  readonly medium?: string;
  readonly bio?: string;
  readonly active?: boolean;
  readonly portraitMediaId?: string;
}

/** A `PATCH` body: a field left out is unchanged; an empty string clears `medium` or `bio`. */
export interface ArtistUpdate {
  readonly name?: string;
  readonly slug?: string;
  readonly medium?: string;
  readonly bio?: string;
  readonly active?: boolean;
  readonly portraitMediaId?: string;
  readonly removePortrait?: true;
}

export interface ArtistListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly status?: PublishStatus;
  readonly search?: string;
}

/** The status commands, as their route segments. */
export type ArtistCommand = ContentCommand;

const isStatus = oneOf(PUBLISH_STATUSES);

/**
 * An int64 (`legacyWordPressId`): the OpenAPI schema allows a number or a numeric string, so both are read. A
 * WordPress term id is far below 2^53, so a number holds it exactly. `undefined` when it is neither.
 */
function decodeInt64(value: unknown): number | null | undefined {
  if (value === null) return null;
  if (isNumber(value) && Number.isSafeInteger(value)) return value;
  if (typeof value === 'string' && /^-?(?:0|[1-9]\d*)$/.test(value)) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) ? parsed : undefined;
  }
  return undefined;
}

export function decodeArtist(body: unknown): Artist | null {
  const f = fields(body);
  if (!f) return null;
  const legacyId = decodeInt64(f['legacyWordPressId']);
  if (
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['slug']) ||
    !isNullableString(f['medium']) ||
    !isNullableString(f['bio']) ||
    !isBoolean(f['active']) ||
    !isNullableString(f['portraitMediaId']) ||
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
    name: f['name'],
    slug: f['slug'],
    medium: f['medium'],
    bio: f['bio'],
    active: f['active'],
    portraitMediaId: f['portraitMediaId'],
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

export function decodeArtistSummary(body: unknown): ArtistSummary | null {
  const f = fields(body);
  if (!f) return null;
  if (
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['slug']) ||
    !isNullableString(f['medium']) ||
    !isBoolean(f['active']) ||
    !isStatus(f['status']) ||
    !isNullableString(f['portraitMediaId']) ||
    !isString(f['updatedAt'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    name: f['name'],
    slug: f['slug'],
    medium: f['medium'],
    active: f['active'],
    status: f['status'],
    portraitMediaId: f['portraitMediaId'],
    updatedAt: f['updatedAt'],
  };
}

export function decodeArtistPage(body: unknown): ArtistPage | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeArtistSummary);
  if (items.some((item) => item === null)) return null;
  return { items: items as ArtistSummary[], nextCursor: f['nextCursor'] };
}

export type ArtistProblem = ApiProblem;

export const decodeArtistProblem = decodeApiProblem;

/** One row of the Deleted view (`ArtistDeletedServiceModel`, ADR 0052). Admin only. */
export interface ArtistDeletedRow {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  /** What it was when it was deleted, not where it is now. */
  readonly status: PublishStatus;
  readonly deletedAt: string;
  /** The row version, sent back as `If-Match` when restoring it. */
  readonly etag: string;
}

export interface ArtistDeletedList {
  readonly items: readonly ArtistDeletedRow[];
  readonly nextCursor: string | null;
}

export function decodeArtistDeletedRow(body: unknown): ArtistDeletedRow | null {
  const f = fields(body);
  if (
    !f ||
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['slug']) ||
    !isStatus(f['status']) ||
    !isString(f['deletedAt']) ||
    !isString(f['etag'])
  ) {
    return null;
  }
  return {
    id: f['id'],
    name: f['name'],
    slug: f['slug'],
    status: f['status'],
    deletedAt: f['deletedAt'],
    etag: f['etag'],
  };
}

export function decodeArtistDeletedList(body: unknown): ArtistDeletedList | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeArtistDeletedRow);
  if (items.some((item) => item === null)) return null;
  return { items: items as ArtistDeletedRow[], nextCursor: f['nextCursor'] };
}
