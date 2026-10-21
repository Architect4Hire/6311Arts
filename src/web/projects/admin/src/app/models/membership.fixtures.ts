import { MembershipPlan, MembershipPlanSummary } from './membership';

/** The spike's Gray tier as the API returns it, for specs. */
export const MEMBERSHIP_PLAN: MembershipPlan = {
  id: '0199f0a0-0000-7000-8000-0000000000b1',
  name: 'Gray',
  slug: 'gray-membership',
  perks: ['5% off every class & workshop', 'Annual membership party', '6311arts tote'],
  discountPercent: 5,
  featured: false,
  displayOrder: 1,
  startingPriceCents: 10_000,
  startingCadence: 'Annual',
  currency: 'USD',
  startingPriceEditable: true,
  priceCents: null,
  cadence: null,
  squareHasRecord: false,
  status: 'Draft',
  publishedAt: null,
  scheduledFor: null,
  sitePath: '/memberships',
  legacyWordPressId: null,
  createdAt: '2026-10-20T12:00:00+00:00',
  updatedAt: '2026-10-20T12:00:00+00:00',
  updatedBy: 'editor-1',
  etag: '"0123456789abcdef0123456789abcdef"',
};

export function toMembershipPlanSummary(plan: MembershipPlan): MembershipPlanSummary {
  return {
    id: plan.id,
    name: plan.name,
    slug: plan.slug,
    status: plan.status,
    discountPercent: plan.discountPercent,
    featured: plan.featured,
    displayOrder: plan.displayOrder,
    perkCount: plan.perks.length,
    startingPriceCents: plan.startingPriceCents,
    priceCents: plan.priceCents,
    cadence: plan.cadence,
    currency: plan.currency,
    updatedAt: plan.updatedAt,
  };
}
