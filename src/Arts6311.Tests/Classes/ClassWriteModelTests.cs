using System.Reflection;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;

namespace Arts6311.Tests.Classes;

/// <summary>
/// The field-ownership guard (square.md, ADR 0032): what Square holds the record of is read-only in the CMS, so no
/// write model may carry it. A field added to a ViewModel with one of these names fails here rather than in review.
/// </summary>
public sealed class ClassWriteModelTests
{
    /// <summary>
    /// The Square-owned projections, by the names they would most plausibly be given. <c>StartingPriceCents</c> is
    /// deliberately not among them: it is the CMS's own draft field, which the first push creates Square's objects
    /// with (ADR 0032).
    /// </summary>
    private static readonly string[] Forbidden =
    [
        "PriceCents", "Price", "SeatsRemaining", "Seats", "SpotsLeft", "SoldOut", "StockOnHand", "Stock",
        "QuantityOnHand", "SquareObjectId", "SquareVariationId", "SquareId",
    ];

    private static readonly Type[] Covered =
    [
        typeof(ClassCreateViewModel),
        typeof(ClassUpdateViewModel),
        typeof(ClassRunViewModel),
        typeof(ClassSessionViewModel),
        typeof(ClassListViewModel),
        typeof(SiteClassListViewModel),
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
        var all = typeof(ClassCreateViewModel).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ClassCreateViewModel).Namespace)
            .Where(type => type.IsClass && !type.IsAbstract);

        Assert.Equal(
            Covered.Select(type => type.Name).Order(StringComparer.Ordinal),
            all.Select(type => type.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheReadModelDoesCarryThemAsReadOnlyProjections()
    {
        // The other half of the rule: staff see what Square holds, they just cannot send it.
        var run = typeof(ClassRunServiceModel).GetProperties().Select(property => property.Name).ToList();

        Assert.Contains("PriceCents", run);
        Assert.Contains("SeatsRemaining", run);
        Assert.Contains("SoldOut", run);
    }
}
