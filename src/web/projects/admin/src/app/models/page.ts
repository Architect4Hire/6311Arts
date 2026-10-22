/**
 * Pages' API shapes, mirroring the API's ServiceModels exactly (`PageServiceModels.cs`, the OpenAPI snapshot): string
 * ids, ISO timestamps, enum names as strings, and the body as the sanitized HTML the API stored.
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

/** The limits the API enforces (`PagePolicy`, `Slugs`), so the form can stop early and say why. */
export const PAGE_LIMITS = {
  titleMaxLength: 200,
  slugMaxLength: SLUG_MAX_LENGTH,
  /** Characters of HTML once sanitized. */
  bodyMaxLength: 100_000,
  searchMaxLength: 100,
} as const;

/**
 * Router state the editor's delete sets and the list it returns to reads (ADR 0052). It lives here rather than in
 * either component, so neither screen's lazy chunk pulls in the other (frontend.md).
 */
export const PAGE_DELETED_STATE = 'pageDeleted';

/** The problem codes the page screens act on: the ones every content type shares. */
export const PAGE_CODES = CONTENT_CODES;

export type PageCommand = ContentCommand;

export interface Page {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  /** Sanitized HTML, exactly as stored; `null` when the page has no text yet. */
  readonly body: string | null;
  /** The library images the body shows. */
  readonly imageMediaIds: readonly string[];
  readonly status: PublishStatus;
  /** When it was first published; kept through an unpublish. */
  readonly publishedAt: string | null;
  readonly scheduledFor: string | null;
  /** The slug can no longer change: the page has been published. */
  readonly slugLocked: boolean;
  readonly legacyWordPressId: number | null;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly updatedBy: string;
  /** The row version, sent back as `If-Match`. */
  readonly etag: string;
}

export interface PageSummary {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly status: PublishStatus;
  readonly updatedAt: string;
}

export interface PageList {
  readonly items: readonly PageSummary[];
  readonly nextCursor: string | null;
}

/** One row of the Deleted view (`PageDeletedServiceModel`, ADR 0052). Admin only. */
export interface PageDeletedRow {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  /** What the page was when it was deleted, not where it is now. */
  readonly status: PublishStatus;
  readonly deletedAt: string;
  /** The row version, sent back as `If-Match` when restoring it. */
  readonly etag: string;
}

export interface PageDeletedList {
  readonly items: readonly PageDeletedRow[];
  readonly nextCursor: string | null;
}

/** A `POST` body. A slug left out is made from the title; the body is sanitized by the API. */
export interface PageCreate {
  readonly title: string;
  readonly slug?: string;
  readonly body?: string;
}

/** A `PATCH` body: a field left out is unchanged; an empty body clears it. */
export interface PageUpdate {
  readonly title?: string;
  readonly slug?: string;
  readonly body?: string;
}

export interface PageListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  readonly status?: PublishStatus;
  readonly search?: string;
}

const isStatus = oneOf(PUBLISH_STATUSES);

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

export function decodePage(body: unknown): Page | null {
  const f = fields(body);
  if (!f) return null;
  const legacyId = decodeInt64(f['legacyWordPressId']);
  const images = f['imageMediaIds'];
  if (
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isNullableString(f['body']) ||
    !Array.isArray(images) ||
    !images.every(isString) ||
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
    body: f['body'],
    imageMediaIds: images,
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

export function decodePageSummary(body: unknown): PageSummary | null {
  const f = fields(body);
  if (
    !f ||
    !isString(f['id']) ||
    !isString(f['title']) ||
    !isString(f['slug']) ||
    !isStatus(f['status']) ||
    !isString(f['updatedAt'])
  ) {
    return null;
  }
  return {
    id: f['id'],
    title: f['title'],
    slug: f['slug'],
    status: f['status'],
    updatedAt: f['updatedAt'],
  };
}

export function decodePageList(body: unknown): PageList | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodePageSummary);
  if (items.some((item) => item === null)) return null;
  return { items: items as PageSummary[], nextCursor: f['nextCursor'] };
}

export function decodePageDeletedRow(body: unknown): PageDeletedRow | null {
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

export function decodePageDeletedList(body: unknown): PageDeletedList | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodePageDeletedRow);
  if (items.some((item) => item === null)) return null;
  return { items: items as PageDeletedRow[], nextCursor: f['nextCursor'] };
}

export type PageProblem = ApiProblem;

export const decodePageProblem = decodeApiProblem;
