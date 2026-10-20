/**
 * The artist roster's API shapes, mirroring the API's ServiceModels exactly (`ArtistServiceModels.cs`, the OpenAPI
 * snapshot): string ids, ISO timestamps, enum names as strings.
 */

import { IconName, StatusTone } from '@6311/ui';
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

/** `PublishStatus`: the content lifecycle every CMS type shares (content.md). */
export const PUBLISH_STATUSES = ['Draft', 'Scheduled', 'Published', 'Archived'] as const;
export type PublishStatus = (typeof PUBLISH_STATUSES)[number];

/** How each status reads in a chip: a word, a glyph and a tone, never colour alone (design-system.md). */
export const STATUS_CHIPS: Readonly<
  Record<
    PublishStatus,
    { readonly label: string; readonly icon: IconName; readonly tone: StatusTone }
  >
> = {
  Draft: { label: 'Draft', icon: 'info', tone: 'neutral' },
  Scheduled: { label: 'Scheduled', icon: 'clock', tone: 'pending' },
  Published: { label: 'Published', icon: 'check', tone: 'positive' },
  Archived: { label: 'Archived', icon: 'close', tone: 'neutral' },
};

/** The limits the API enforces (`ArtistPolicy`, `Slugs`), so the form can stop early and say why. */
export const ARTIST_LIMITS = {
  nameMaxLength: 200,
  slugMaxLength: 120,
  mediumMaxLength: 100,
  bioMaxLength: 5000,
  searchMaxLength: 100,
} as const;

/** The problem codes the artist screens act on (`ContentErrorCodes`, `ErrorCodes`). */
export const ARTIST_CODES = {
  slugTaken: 'cms.slug_taken',
  slugLocked: 'cms.slug_locked',
  transitionInvalid: 'cms.status_transition_invalid',
  concurrencyConflict: 'concurrency.conflict',
  notFound: 'resource.not_found',
  validationFailed: 'validation.failed',
} as const;

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
  readonly status?: PublishStatus;
  readonly search?: string;
}

/** The status commands, as their route segments. */
export type ArtistCommand = 'publish' | 'unpublish' | 'archive';

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

/**
 * The slug the API makes from a name when none is sent (`Slugs.FromName`): accents dropped, every other run of
 * non-alphanumerics one hyphen, none at either end. Shown as the form's suggestion; the API has the last word.
 */
export function slugFromName(name: string): string {
  const slug = name
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return slug.length <= ARTIST_LIMITS.slugMaxLength
    ? slug
    : slug.slice(0, ARTIST_LIMITS.slugMaxLength).replace(/-+$/, '');
}

/** The shape the API accepts as a slug (`Slugs.IsValid`). */
export function isSlug(value: string): boolean {
  return (
    value.length > 0 &&
    value.length <= ARTIST_LIMITS.slugMaxLength &&
    /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value)
  );
}
