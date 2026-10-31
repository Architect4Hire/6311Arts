/**
 * The instructors' API shapes, mirroring the API's ServiceModels exactly (`InstructorServiceModels.cs`, the OpenAPI
 * snapshot): string ids, ISO timestamps, enum names as strings. An instructor is a content type of its own (ADR 0043),
 * shaped like an artist, with a website, an Instagram handle and an optional link to the artist they also are.
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
  nullable,
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
  return (
    url.protocol === 'https:' && url.hostname !== '' && url.username === '' && url.password === ''
  );
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
export type InstructorArtist = Credit;

export interface Instructor extends ContentRecord {
  readonly name: string;
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
  /** It has been published, so changing its web address redirects the old one (ADR 0035). */
  readonly slugLocked: boolean;
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

export type InstructorPage = CursorPage<InstructorSummary>;

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

export function decodeInstructor(body: unknown): Instructor | null {
  return decodeShape(body, {
    ...CONTENT_FIELDS,
    name: text,
    medium: optionalText,
    bio: optionalText,
    active: flag,
    portraitMediaId: optionalText,
    website: optionalText,
    instagram: optionalText,
    artist: nullable(credit),
    slugLocked: flag,
  });
}

export function decodeInstructorSummary(body: unknown): InstructorSummary | null {
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

export function decodeInstructorPage(body: unknown): InstructorPage | null {
  return decodeCursorPage(body, decodeInstructorSummary);
}

export type InstructorProblem = ApiProblem;

export const decodeInstructorProblem = decodeApiProblem;

/** One row of the Deleted view (`InstructorDeletedServiceModel`, ADR 0052). Admin only. */
export type InstructorDeletedRow = DeletedRow<'name'>;

export type InstructorDeletedList = DeletedList<'name'>;

export function decodeInstructorDeletedList(body: unknown): InstructorDeletedList | null {
  return decodeDeletedList(body, 'name');
}
