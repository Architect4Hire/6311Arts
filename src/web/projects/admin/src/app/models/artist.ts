/**
 * The artist roster's API shapes, mirroring the API's ServiceModels exactly (`ArtistServiceModels.cs`, the OpenAPI
 * snapshot): string ids, ISO timestamps, enum names as strings.
 */

import {
  CONTENT_CODES,
  CONTENT_FIELDS,
  ContentCommand,
  ContentRecord,
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
  optionalText,
  text,
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

export interface Artist extends ContentRecord {
  readonly name: string;
  readonly medium: string | null;
  readonly bio: string | null;
  readonly active: boolean;
  readonly portraitMediaId: string | null;
  /** The slug can no longer change: the artist has been published. */
  readonly slugLocked: boolean;
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

export type ArtistPage = CursorPage<ArtistSummary>;

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

export function decodeArtist(body: unknown): Artist | null {
  return decodeShape(body, {
    ...CONTENT_FIELDS,
    name: text,
    medium: optionalText,
    bio: optionalText,
    active: flag,
    portraitMediaId: optionalText,
    slugLocked: flag,
  });
}

export function decodeArtistSummary(body: unknown): ArtistSummary | null {
  return decodeShape(body, {
    id: text,
    name: text,
    slug: text,
    medium: optionalText,
    active: flag,
    status,
    portraitMediaId: optionalText,
    updatedAt: text,
  });
}

export function decodeArtistPage(body: unknown): ArtistPage | null {
  return decodeCursorPage(body, decodeArtistSummary);
}

export type ArtistProblem = ApiProblem;

export const decodeArtistProblem = decodeApiProblem;

/** One row of the Deleted view (`ArtistDeletedServiceModel`, ADR 0052). Admin only. */
export type ArtistDeletedRow = DeletedRow<'name'>;

export type ArtistDeletedList = DeletedList<'name'>;

export function decodeArtistDeletedList(body: unknown): ArtistDeletedList | null {
  return decodeDeletedList(body, 'name');
}
