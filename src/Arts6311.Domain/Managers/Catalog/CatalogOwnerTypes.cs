namespace Arts6311.Domain.Managers.Catalog;

/// <summary>
/// The kinds of record that hold a SKU (ADR 0032, 0037). Strings, not module types: this is the shared kernel, and
/// it depends on no module (<c>backend.md</c>).
/// </summary>
public static class CatalogOwnerTypes
{
    /// <summary>One sitting of a class. Each is its own Square variation, with its own SKU.</summary>
    public const string ClassSession = "class-session";

    /// <summary>Artwork in the shop (2.10). Merch is POS-only and never reaches the CMS (ADR 0032).</summary>
    public const string ShopItem = "shop-item";

    /// <summary>The longest owner type stored, so the column is sized once.</summary>
    internal const int MaxLength = 32;
}
