/**
 * Redirects' API shapes, mirroring the API's ServiceModels exactly (`RedirectServiceModels.cs`, the OpenAPI snapshot):
 * addresses in the API's normal form, enum names as strings, ISO timestamps.
 */

import { IconName, StatusTone } from '@6311/ui';
import {
  ApiProblem,
  CursorPage,
  decodeApiProblem,
  decodeCursorPage,
  fields,
  isNumber,
  isString,
  oneOf,
} from './decode';

/** The limits the API enforces (`RedirectPolicy`), so the form can stop early and say why. */
export const REDIRECT_LIMITS = {
  fromMaxLength: 400,
  toMaxLength: 800,
  searchMaxLength: 100,
} as const;

export const REDIRECT_STATUS_CODES = [301, 302] as const;
export type RedirectStatusCode = (typeof REDIRECT_STATUS_CODES)[number];

/** `RedirectSource`. */
export const REDIRECT_SOURCES = ['Manual', 'Imported', 'SlugChange'] as const;
export type RedirectSource = (typeof REDIRECT_SOURCES)[number];

/** How each source reads: a word, never colour alone (design-system.md). */
export const SOURCE_CHIPS: Readonly<
  Record<
    RedirectSource,
    { readonly label: string; readonly icon: IconName; readonly tone: StatusTone }
  >
> = {
  Manual: { label: 'Added by staff', icon: 'people', tone: 'neutral' },
  Imported: { label: 'From the old site', icon: 'arrow', tone: 'neutral' },
  SlugChange: { label: 'Address change', icon: 'spark', tone: 'pending' },
};

/** The problem codes the redirect screens act on (`RedirectErrorCodes`, `ErrorCodes`). */
export const REDIRECT_CODES = {
  fromTaken: 'redirects.from_taken',
  loopOrChain: 'redirects.loop_or_chain',
  concurrencyConflict: 'concurrency.conflict',
  notFound: 'resource.not_found',
  validationFailed: 'validation.failed',
} as const;

export interface Redirect {
  readonly id: string;
  /** The old address, in normal form: a lower-case site path, with any query. */
  readonly fromPath: string;
  /** A site path (with any query and fragment) or a full `https://` address. */
  readonly toPath: string;
  readonly statusCode: number;
  readonly source: RedirectSource;
  readonly legacyWordPressId: number | null;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly updatedBy: string;
  /** The row version, sent back as `If-Match`. */
  readonly etag: string;
}

export type RedirectList = CursorPage<Redirect>;

export interface RedirectCreate {
  readonly fromPath: string;
  readonly toPath: string;
  readonly statusCode?: number;
}

export interface RedirectUpdate {
  readonly fromPath?: string;
  readonly toPath?: string;
  readonly statusCode?: number;
}

export interface RedirectListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly source?: RedirectSource;
  readonly search?: string;
}

const isSource = oneOf(REDIRECT_SOURCES);

/** An int (`statusCode`) or int64 (`legacyWordPressId`): a number or a numeric string, as the schema allows. */
function decodeInteger(value: unknown): number | null | undefined {
  if (value === null) return null;
  if (isNumber(value) && Number.isSafeInteger(value)) return value;
  if (typeof value === 'string' && /^-?(?:0|[1-9]\d*)$/.test(value)) {
    const parsed = Number(value);
    return Number.isSafeInteger(parsed) ? parsed : undefined;
  }
  return undefined;
}

export function decodeRedirect(body: unknown): Redirect | null {
  const f = fields(body);
  if (!f) return null;
  const statusCode = decodeInteger(f['statusCode']);
  const legacyId = decodeInteger(f['legacyWordPressId']);
  if (
    !isString(f['id']) ||
    !isString(f['fromPath']) ||
    !isString(f['toPath']) ||
    statusCode === undefined ||
    statusCode === null ||
    !isSource(f['source']) ||
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
    fromPath: f['fromPath'],
    toPath: f['toPath'],
    statusCode,
    source: f['source'],
    legacyWordPressId: legacyId,
    createdAt: f['createdAt'],
    updatedAt: f['updatedAt'],
    updatedBy: f['updatedBy'],
    etag: f['etag'],
  };
}

export function decodeRedirectList(body: unknown): RedirectList | null {
  return decodeCursorPage(body, decodeRedirect);
}

export type RedirectProblem = ApiProblem;

export const decodeRedirectProblem = decodeApiProblem;
