/**
 * Pages' API shapes, mirroring the API's ServiceModels exactly (`PageServiceModels.cs`, the OpenAPI snapshot): string
 * ids, ISO timestamps, enum names as strings, and the body as the sanitized HTML the API stored.
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
  listOf,
  optionalText,
  text,
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

export interface Page extends ContentRecord {
  readonly title: string;
  /** Sanitized HTML, exactly as stored; `null` when the page has no text yet. */
  readonly body: string | null;
  /** The library images the body shows. */
  readonly imageMediaIds: readonly string[];
  /** The slug can no longer change: the page has been published. */
  readonly slugLocked: boolean;
}

export interface PageSummary {
  readonly id: string;
  readonly title: string;
  readonly slug: string;
  readonly status: PublishStatus;
  readonly updatedAt: string;
}

export type PageList = CursorPage<PageSummary>;

/** One row of the Deleted view (`PageDeletedServiceModel`, ADR 0052). Admin only. */
export type PageDeletedRow = DeletedRow<'title'>;

export type PageDeletedList = DeletedList<'title'>;

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
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly status?: PublishStatus;
  readonly search?: string;
}

export function decodePage(body: unknown): Page | null {
  return decodeShape(body, {
    ...CONTENT_FIELDS,
    title: text,
    body: optionalText,
    imageMediaIds: listOf(text),
    slugLocked: flag,
  });
}

export function decodePageSummary(body: unknown): PageSummary | null {
  return decodeShape(body, { id: text, title: text, slug: text, status, updatedAt: text });
}

export function decodePageList(body: unknown): PageList | null {
  return decodeCursorPage(body, decodePageSummary);
}

export function decodePageDeletedList(body: unknown): PageDeletedList | null {
  return decodeDeletedList(body, 'title');
}

export type PageProblem = ApiProblem;

export const decodePageProblem = decodeApiProblem;
