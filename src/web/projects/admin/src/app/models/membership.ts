/**
 * Memberships' API shapes, mirroring the API's ServiceModels exactly (`MembershipPlanServiceModels.cs`, the OpenAPI
 * snapshot): string ids, ISO timestamps, enum names as strings, integer cents with a currency.
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
  amount,
  ApiProblem,
  CursorPage,
  decodeApiProblem,
  decodeCursorPage,
  decodeShape,
  field,
  flag,
  int64,
  listOf,
  oneOf,
  optionalText,
  text,
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

export interface MembershipPlan extends ContentRecord {
  readonly name: string;
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
  /** Where the site shows it: the page every tier shares. */
  readonly sitePath: string;
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

export type MembershipPlanList = CursorPage<MembershipPlanSummary>;

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

const isCadence = oneOf(MEMBERSHIP_CADENCES.map((cadence) => cadence.value));

export function decodeMembershipPlan(body: unknown): MembershipPlan | null {
  return decodeShape(body, {
    ...CONTENT_FIELDS,
    name: text,
    perks: listOf(text),
    discountPercent: amount,
    featured: flag,
    displayOrder: amount,
    startingPriceCents: int64,
    startingCadence: field(isCadence),
    currency: text,
    startingPriceEditable: flag,
    priceCents: int64,
    cadence: optionalText,
    squareHasRecord: flag,
    sitePath: text,
  });
}

export function decodeMembershipPlanSummary(body: unknown): MembershipPlanSummary | null {
  return decodeShape(body, {
    id: text,
    name: text,
    slug: text,
    status,
    discountPercent: amount,
    featured: flag,
    displayOrder: amount,
    perkCount: amount,
    startingPriceCents: int64,
    priceCents: int64,
    cadence: optionalText,
    currency: text,
    updatedAt: text,
  });
}

export function decodeMembershipPlanList(body: unknown): MembershipPlanList | null {
  return decodeCursorPage(body, decodeMembershipPlanSummary);
}

export type MembershipProblem = ApiProblem;

export const decodeMembershipProblem = decodeApiProblem;

/** One row of the Deleted view (`MembershipPlanDeletedServiceModel`, ADR 0052). Admin only. */
export type MembershipPlanDeletedRow = DeletedRow<'name'>;

export type MembershipPlanDeletedList = DeletedList<'name'>;

export function decodeMembershipPlanDeletedList(body: unknown): MembershipPlanDeletedList | null {
  return decodeDeletedList(body, 'name');
}

/** Square's own cadence value (`ANNUAL`, `EVERY_TWO_MONTHS`) in words. */
export function cadenceWords(cadence: string): string {
  const words = cadence.toLowerCase().replace(/_/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}
