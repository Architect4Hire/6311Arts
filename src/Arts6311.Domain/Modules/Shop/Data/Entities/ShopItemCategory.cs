namespace Arts6311.Domain.Modules.Shop.Data.Entities;

/// <summary>
/// One category a shop item is filed under, from the allow-list in <c>ShopPolicy.Categories</c> (ADR 0038): a slug
/// the site filters on and the 5.x push maps to a Square category.
/// </summary>
public sealed class ShopItemCategory
{
    public Guid ShopItemId { get; set; }

    /// <summary>The category's slug, for example <c>jewelry</c>. Always one the allow-list contains.</summary>
    public string Category { get; set; } = string.Empty;
}
