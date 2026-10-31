using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Memberships.Managers;

/// <summary>
/// The membership tiers' sortable columns (ADR 0062). In staff's display order unless asked, which no column shows:
/// it is the order <c>/memberships</c> lists them in. "price" is the figure the table shows — Square's once Square
/// holds the plan, the starting price before — and reading it for an order writes nothing.
/// </summary>
internal static class MembershipPlanListOrder
{
    public static readonly ListOrder<MembershipPlan, MembershipPlanSummaryServiceModel> Columns =
        new ListOrder<MembershipPlan, MembershipPlanSummaryServiceModel>(plan => plan.Id, row => row.Id, ListSort.Ascending("order"))
            .By("order", plan => plan.DisplayOrder, row => row.DisplayOrder)
            .By("tier", plan => plan.Name, row => row.Name)
            .By("status", plan => plan.Status, row => row.Status)
            .By("price", plan => plan.PriceCents ?? plan.StartingPriceCents, row => row.PriceCents ?? row.StartingPriceCents)
            .By("discount", plan => plan.DiscountPercent, row => row.DiscountPercent)
            .By("updated", plan => plan.UpdatedAt, row => row.UpdatedAt);
}
