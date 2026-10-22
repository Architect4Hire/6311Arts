using System.Reflection;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;

namespace Arts6311.Tests.Shop;

/// <summary>
/// The field-ownership guard (square.md, ADR 0032): what Square holds the record of is read-only in the CMS, so no
/// write model may carry it. A field added to a ViewModel with one of these names fails here rather than in review.
/// </summary>
public sealed class ShopItemWriteModelTests
{
    /// <summary>
    /// The Square-owned projections, by the names they would most plausibly be given. <c>StartingPriceCents</c> and
    /// <c>StartingStockQuantity</c> are deliberately not among them: they are the CMS's own draft fields, which the
    /// first push creates Square's objects with (ADR 0032).
    /// </summary>
    private static readonly string[] Forbidden =
    [
        "PriceCents", "Price", "StockOnHand", "Stock", "QuantityOnHand", "Quantity", "SoldOut", "SeatsRemaining",
        "SquareObjectId", "SquareVariationId", "SquareId", "InStock",
    ];

    private static readonly Type[] Covered =
    [
        typeof(ShopItemCreateViewModel),
        typeof(ShopItemUpdateViewModel),
        typeof(ShopItemListViewModel),
        typeof(SiteShopListViewModel),

        // The CSV import's text carries only the starting figures, never Square's record: its columns are
        // ShopImportColumns, and no Square-owned field has one (ADR 0053).
        typeof(ShopImportViewModel),
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
        var all = typeof(ShopItemCreateViewModel).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ShopItemCreateViewModel).Namespace)
            .Where(type => type.IsClass && !type.IsAbstract);

        Assert.Equal(
            Covered.Select(type => type.Name).Order(StringComparer.Ordinal),
            all.Select(type => type.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheReadModelsDoCarryThemAsReadOnlyProjections()
    {
        // The other half of the rule: staff see what Square holds, they just cannot send it.
        var item = typeof(ShopItemServiceModel).GetProperties().Select(property => property.Name).ToList();
        var row = typeof(ShopItemSummaryServiceModel).GetProperties().Select(property => property.Name).ToList();

        Assert.Contains("PriceCents", item);
        Assert.Contains("StockOnHand", item);
        Assert.Contains("SoldOut", item);
        Assert.Contains("PriceCents", row);
        Assert.Contains("StockOnHand", row);
    }
}
