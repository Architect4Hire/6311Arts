/**
 * The instructors' API shapes, mirroring the API's ServiceModels exactly (`InstructorServiceModels.cs`, the OpenAPI
 * snapshot): string ids, ISO timestamps, enum names as strings. An instructor is a content type of its own (ADR 0043),
 * shaped like an artist, with a website, an Instagram handle and an optional link to the artist they also are.
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

/** The limits the API enforces (`InstructorPolicy`, `Slugs`), so the form can stop early and say why. */
export const INSTRUCTOR_LIMITS = {
  nameMaxLength: 200,
  slugMaxLength: SLUG_MAX_LENGTH,
  mediumMaxLength: 100,
  bioMaxLength: 5000,
  websiteMaxLength: 300,
  instagramMaxLength: 30,
  searchMaxLength: 100,
} as const;

/**
 * Whether the API will take this as a website (`InstructorPolicy.IsWebsite`): an absolute `https:` address with a host
 * and no user name or password in it.
 */
export function isWebsite(value: string): boolean {
  if (value.length > INSTRUCTOR_LIMITS.websiteMaxLength) return false;
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return false;
  }
  return url.protocol === 'https:' && url.hostname !== '' && url.username === '' && url.password === '';
}

/** A handle as staff may type it, in the one spelling the API keeps (`InstructorPolicy.NormalizeInstagram`). */
export function normalizeInstagram(value: string): string {
  return value.trim().replace(/^@+/, '').toLowerCase();
}

/** Instagram's handle shape, once normalized (`InstructorPolicy.IsInstagramHandle`). */
export function isInstagramHandle(value: string): boolean {
  return /^[a-z0-9._]{1,30}$/.test(value);
}

/** The problem codes the instructor screens act on: the ones every content type shares. */
export const INSTRUCTOR_CODES = CONTENT_CODES;

/** The artist an instructor is linked to, as the Artists module describes them now. */
export interface InstructorArtist {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly status: PublishStatus;
}

export interface Instructor {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly medium: string | null;
  readonly bio: string | null;
  readonly active: boolean;
  readonly portraitMediaId: string | null;
  /** An absolute `https:` address. */
  readonly website: string | null;
  /** The handle, without the `@`. */
  readonly instagram: string | null;
  /** The artist they also are; `null` when not linked. */
  readonly artist: InstructorArtist | null;
  readonly status: PublishStatus;
  /** When it was first published; kept through an unpublish. */
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

export interface InstructorSummary {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly medium: string | null;
  readonly active: boolean;
  readonly status: PublishStatus;
  readonly portraitMediaId: string | null;
  readonly updatedAt: string;
}

export interface InstructorPage {
  readonly items: readonly InstructorSummary[];
  readonly nextCursor: string | null;
}

/** A `POST` body. A slug left out is made from the name. */
export interface InstructorCreate {
  readonly name: string;
  readonly slug?: string;
  readonly medium?: string;
  readonly bio?: string;
  readonly active?: boolean;
  readonly portraitMediaId?: string;
  readonly website?: string;
  readonly instagram?: string;
  readonly artistId?: string;
}

/** A `PATCH` body: a field left out is unchanged; an empty string clears `medium`, `bio`, `website` or `instagram`. */
export interface InstructorUpdate {
  readonly name?: string;
  readonly slug?: string;
  readonly medium?: string;
  readonly bio?: string;
  readonly active?: boolean;
  readonly portraitMediaId?: string;
  readonly removePortrait?: true;
  readonly website?: string;
  readonly instagram?: string;
  readonly artistId?: string;
  readonly removeArtist?: true;
}

export interface InstructorListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly status?: PublishStatus;
  readonly search?: string;
}

/** The status commands, as their route segments. */
export type InstructorCommand = ContentCommand;

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

function decodeInstructorArtist(body: unknown): InstructorArtist | null | undefined {
  if (body === null) return null;
  const f = fields(body);
  if (!f || !isString(f['id']) || !isString(f['name']) || !isString(f['slug']) || !isStatus(f['status'])) {
    return undefined;
  }
  return { id: f['id'], name: f['name'], slug: f['slug'], status: f['status'] };
}

export function decodeInstructor(body: unknown): Instructor | null {
  const f = fields(body);
  if (!f) return null;
  const legacyId = decodeInt64(f['legacyWordPressId']);
  const artist = decodeInstructorArtist(f['artist']);
  if (
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['slug']) ||
    !isNullableString(f['medium']) ||
    !isNullableString(f['bio']) ||
    !isBoolean(f['active']) ||
    !isNullableString(f['portraitMediaId']) ||
    !isNullableString(f['website']) ||
    !isNullableString(f['instagram']) ||
    artist === undefined ||
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
    website: f['website'],
    instagram: f['instagram'],
    artist,
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

export function decodeInstructorSummary(body: unknown): InstructorSummary | null {
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

export function decodeInstructorPage(body: unknown): InstructorPage | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeInstructorSummary);
  if (items.some((item) => item === null)) return null;
  return { items: items as InstructorSummary[], nextCursor: f['nextCursor'] };
}

export type InstructorProblem = ApiProblem;

export const decodeInstructorProblem = decodeApiProblem;

/** One row of the Deleted view (`InstructorDeletedServiceModel`, ADR 0052). Admin only. */
export interface InstructorDeletedRow {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  /** What it was when it was deleted, not where it is now. */
  readonly status: PublishStatus;
  readonly deletedAt: string;
  /** The row version, sent back as `If-Match` when restoring it. */
  readonly etag: string;
}

export interface InstructorDeletedList {
  readonly items: readonly InstructorDeletedRow[];
  readonly nextCursor: string | null;
}

export function decodeInstructorDeletedRow(body: unknown): InstructorDeletedRow | null {
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

export function decodeInstructorDeletedList(body: unknown): InstructorDeletedList | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeInstructorDeletedRow);
  if (items.some((item) => item === null)) return null;
  return { items: items as InstructorDeletedRow[], nextCursor: f['nextCursor'] };
}
