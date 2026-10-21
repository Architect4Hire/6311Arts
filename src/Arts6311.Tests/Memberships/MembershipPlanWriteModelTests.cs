using System.Reflection;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;

namespace Arts6311.Tests.Memberships;

/// <summary>
/// The field-ownership guard (square.md, ADR 0005): what Square holds the record of is read-only in the CMS, so no
/// write model may carry it. A field added to a ViewModel with one of these names fails here rather than in review.
/// </summary>
public sealed class MembershipPlanWriteModelTests
{
    /// <summary>
    /// The Square-owned projections, by the names they would most plausibly be given.
    /// <c>StartingPriceCents</c> and <c>StartingCadence</c> are deliberately not among them: they are the CMS's own
    /// draft fields, which the first push creates the Square plan with (ADR 0005).
    /// </summary>
    private static readonly string[] Forbidden =
    [
        "PriceCents", "Price", "Cadence", "BillingCadence", "SoldOut", "MemberStatus", "MemberCount",
        "SubscriptionId", "SquarePlanId", "SquareObjectId", "SquareVariationId", "SquareId",
    ];

    private static readonly Type[] Covered =
    [
        typeof(MembershipPlanCreateViewModel),
        typeof(MembershipPlanUpdateViewModel),
        typeof(MembershipPlanListViewModel),
    ];

    public static TheoryData<Type> WriteModels => [.. Covered];

    [Theory]
    [MemberData(nameof(WriteModels))]
    public void AWriteModelCarriesNothingSquareOwns(Type model)
    {
        var properties = model.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);

        Assert.Empty(properties.Intersect(Forbidden, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryWriteModelInTheModuleIsCovered()
    {
        // So a ViewModel added later cannot quietly escape the check above.
        var all = typeof(MembershipPlanCreateViewModel).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(MembershipPlanCreateViewModel).Namespace)
            .Where(type => type.IsClass && !type.IsAbstract);

        Assert.Equal(
            Covered.Select(type => type.Name).Order(StringComparer.Ordinal),
            all.Select(type => type.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheReadModelsDoCarryThemAsReadOnlyProjections()
    {
        // The other half of the rule: staff see what Square holds, they just cannot send it.
        var plan = typeof(MembershipPlanServiceModel).GetProperties().Select(property => property.Name).ToList();
        var row = typeof(MembershipPlanSummaryServiceModel).GetProperties().Select(property => property.Name).ToList();

        Assert.Contains("PriceCents", plan);
        Assert.Contains("Cadence", plan);
        Assert.Contains("StartingPriceEditable", plan);
        Assert.Contains("PriceCents", row);
        Assert.Contains("Cadence", row);
    }
}
