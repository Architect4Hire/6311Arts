/**
 * Memberships' API shapes, mirroring the API's ServiceModels exactly (`MembershipPlanServiceModels.cs`, the OpenAPI
 * snapshot): string ids, ISO timestamps, enum names as strings, integer cents with a currency.
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

/** The limits the API enforces (`MembershipPolicy`, `Slugs`), so the form can stop early and say why. */
export const MEMBERSHIP_LIMITS = {
  nameMaxLength: 100,
  slugMaxLength: SLUG_MAX_LENGTH,
  perkMaxLength: 200,
  perksMaxCount: 12,
  discountMaxPercent: 100,
  displayOrderMax: 100,
  /** Cents: $5,000, the typo guard rather than a business rule. */
  startingPriceMaxCents: 500_000,
  searchMaxLength: 100,
} as const;

/** The billing cadences the CMS offers (`MembershipCadence`), with the words staff read. */
export const MEMBERSHIP_CADENCES = [
  { value: 'Annual', label: 'Once a year' },
  { value: 'Monthly', label: 'Every month' },
] as const;

export type MembershipCadence = (typeof MEMBERSHIP_CADENCES)[number]['value'];

/** The problem codes the membership screens act on: the ones every content type shares. */
export const MEMBERSHIP_CODES = CONTENT_CODES;

export type MembershipCommand = ContentCommand;

/** The status commands with no body, which go through `MembershipService.command`. */
export type MembershipPlainCommand = Exclude<MembershipCommand, 'schedule'>;

export interface MembershipPlan {
  readonly id: string;
  readonly name: string;
  /** A stable key, not an address: every tier is shown on `/memberships` (ADR 0040). */
  readonly slug: string;
  /** What the tier includes, in the order the page lists them. */
  readonly perks: readonly string[];
  /** What a member saves on every class, as whole percent. */
  readonly discountPercent: number;
  readonly featured: boolean;
  readonly displayOrder: number;
  /** The CMS's draft price, the figure the first Square push creates the plan with. */
  readonly startingPriceCents: number | null;
  readonly startingCadence: MembershipCadence;
  readonly currency: string;
  /** `false` once Square has reported money or a cadence: both are Square's from then on (ADR 0005). */
  readonly startingPriceEditable: boolean;
  /** Square's record of what the plan bills; `null` until Square has reported it. */
  readonly priceCents: number | null;
  /** Square's record of how often it bills, in Square's own words; `null` until the first pull. */
  readonly cadence: string | null;
  readonly squareHasRecord: boolean;
  readonly status: PublishStatus;
  readonly publishedAt: string | null;
  readonly scheduledFor: string | null;
  /** Where the site shows it: the page every tier shares. */
  readonly sitePath: string;
  readonly legacyWordPressId: number | null;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly updatedBy: string;
  /** The row version, sent back as `If-Match`. */
  readonly etag: string;
}

export interface MembershipPlanSummary {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  readonly status: PublishStatus;
  readonly discountPercent: number;
  readonly featured: boolean;
  readonly displayOrder: number;
  readonly perkCount: number;
  readonly startingPriceCents: number | null;
  readonly priceCents: number | null;
  readonly cadence: string | null;
  readonly currency: string;
  readonly updatedAt: string;
}

export interface MembershipPlanList {
  readonly items: readonly MembershipPlanSummary[];
  readonly nextCursor: string | null;
}

/** A `POST` body. A slug left out is made from the name; a tier with no order goes last. */
export interface MembershipPlanCreate {
  readonly name: string;
  readonly slug?: string;
  readonly perks?: readonly string[];
  readonly discountPercent?: number;
  readonly featured?: boolean;
  readonly displayOrder?: number;
  readonly startingPriceCents?: number;
  readonly startingCadence?: MembershipCadence;
}

/** A `PATCH` body: a field left out is unchanged; `perks` replaces the list. */
export interface MembershipPlanUpdate {
  readonly name?: string;
  readonly slug?: string;
  readonly perks?: readonly string[];
  readonly discountPercent?: number;
  readonly featured?: boolean;
  readonly displayOrder?: number;
  readonly startingPriceCents?: number;
  readonly removeStartingPrice?: boolean;
  readonly startingCadence?: MembershipCadence;
}

export interface MembershipPlanListQuery {
  readonly cursor?: string | null;
  readonly limit?: number;
  /** A column the list sorts by, `-` before it for descending (ADR 0062). Absent: the list's own order. */
  readonly sort?: string;
  readonly status?: PublishStatus;
  readonly search?: string;
}

const isStatus = oneOf(PUBLISH_STATUSES);
const isCadence = oneOf(MEMBERSHIP_CADENCES.map((cadence) => cadence.value));

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

export function decodeMembershipPlan(body: unknown): MembershipPlan | null {
  const f = fields(body);
  if (!f) return null;
  const legacyId = decodeInt64(f['legacyWordPressId']);
  const startingPrice = decodeInt64(f['startingPriceCents']);
  const price = decodeInt64(f['priceCents']);
  const perks = f['perks'];
  if (
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['slug']) ||
    !Array.isArray(perks) ||
    !perks.every(isString) ||
    !isNumber(f['discountPercent']) ||
    !isBoolean(f['featured']) ||
    !isNumber(f['displayOrder']) ||
    startingPrice === undefined ||
    !isCadence(f['startingCadence']) ||
    !isString(f['currency']) ||
    !isBoolean(f['startingPriceEditable']) ||
    price === undefined ||
    !isNullableString(f['cadence']) ||
    !isBoolean(f['squareHasRecord']) ||
    !isStatus(f['status']) ||
    !isNullableString(f['publishedAt']) ||
    !isNullableString(f['scheduledFor']) ||
    !isString(f['sitePath']) ||
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
    perks,
    discountPercent: f['discountPercent'],
    featured: f['featured'],
    displayOrder: f['displayOrder'],
    startingPriceCents: startingPrice,
    startingCadence: f['startingCadence'],
    currency: f['currency'],
    startingPriceEditable: f['startingPriceEditable'],
    priceCents: price,
    cadence: f['cadence'],
    squareHasRecord: f['squareHasRecord'],
    status: f['status'],
    publishedAt: f['publishedAt'],
    scheduledFor: f['scheduledFor'],
    sitePath: f['sitePath'],
    legacyWordPressId: legacyId,
    createdAt: f['createdAt'],
    updatedAt: f['updatedAt'],
    updatedBy: f['updatedBy'],
    etag: f['etag'],
  };
}

export function decodeMembershipPlanSummary(body: unknown): MembershipPlanSummary | null {
  const f = fields(body);
  if (!f) return null;
  const startingPrice = decodeInt64(f['startingPriceCents']);
  const price = decodeInt64(f['priceCents']);
  if (
    !isString(f['id']) ||
    !isString(f['name']) ||
    !isString(f['slug']) ||
    !isStatus(f['status']) ||
    !isNumber(f['discountPercent']) ||
    !isBoolean(f['featured']) ||
    !isNumber(f['displayOrder']) ||
    !isNumber(f['perkCount']) ||
    startingPrice === undefined ||
    price === undefined ||
    !isNullableString(f['cadence']) ||
    !isString(f['currency']) ||
    !isString(f['updatedAt'])
  ) {
    return null;
  }

  return {
    id: f['id'],
    name: f['name'],
    slug: f['slug'],
    status: f['status'],
    discountPercent: f['discountPercent'],
    featured: f['featured'],
    displayOrder: f['displayOrder'],
    perkCount: f['perkCount'],
    startingPriceCents: startingPrice,
    priceCents: price,
    cadence: f['cadence'],
    currency: f['currency'],
    updatedAt: f['updatedAt'],
  };
}

export function decodeMembershipPlanList(body: unknown): MembershipPlanList | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeMembershipPlanSummary);
  if (items.some((item) => item === null)) return null;
  return { items: items as MembershipPlanSummary[], nextCursor: f['nextCursor'] };
}

export type MembershipProblem = ApiProblem;

export const decodeMembershipProblem = decodeApiProblem;

/** One row of the Deleted view (`MembershipPlanDeletedServiceModel`, ADR 0052). Admin only. */
export interface MembershipPlanDeletedRow {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  /** What it was when it was deleted, not where it is now. */
  readonly status: PublishStatus;
  readonly deletedAt: string;
  /** The row version, sent back as `If-Match` when restoring it. */
  readonly etag: string;
}

export interface MembershipPlanDeletedList {
  readonly items: readonly MembershipPlanDeletedRow[];
  readonly nextCursor: string | null;
}

export function decodeMembershipPlanDeletedRow(body: unknown): MembershipPlanDeletedRow | null {
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

export function decodeMembershipPlanDeletedList(body: unknown): MembershipPlanDeletedList | null {
  const f = fields(body);
  if (!f || !Array.isArray(f['items']) || !isNullableString(f['nextCursor'])) return null;
  const items = f['items'].map(decodeMembershipPlanDeletedRow);
  if (items.some((item) => item === null)) return null;
  return { items: items as MembershipPlanDeletedRow[], nextCursor: f['nextCursor'] };
}
