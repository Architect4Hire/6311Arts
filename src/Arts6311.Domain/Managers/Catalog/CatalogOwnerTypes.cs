namespace Arts6311.Domain.Managers.Catalog;

/// <summary>
/// The kinds of record that hold a SKU (ADR 0032, 0037). Strings, not module types: this is the shared kernel, and
/// it depends on no module (<c>backend.md</c>).
/// </summary>
public static class CatalogOwnerTypes
{
    /// <summary>
    /// One run of a class: a term with its own instructor, dates and seats. Each is its own Square variation, with its
    /// own SKU (ADR 0044; before it, each sitting was).
    /// </summary>
    public const string ClassRun = "class-run";

    /// <summary>Artwork in the shop (2.10). Merch is POS-only and never reaches the CMS (ADR 0032).</summary>
    public const string ShopItem = "shop-item";

    /// <summary>The longest owner type stored, so the column is sized once.</summary>
    internal const int MaxLength = 32;
}
