using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>Field limits for a shop item. The admin's form uses the same numbers (<c>SHOP_LIMITS</c>).</summary>
public static class ShopPolicy
{
    public const int TitleMaxLength = 200;

    /// <summary>
    /// The longest description stored, after sanitizing. Beyond <c>nvarchar</c>'s 4,000, so the column is
    /// <c>nvarchar(max)</c>.
    /// </summary>
    public const int DescriptionMaxLength = 100_000;

    /// <summary>
    /// The longest description accepted before sanitizing. Generous, since markup the sanitizer removes counts
    /// here; within the API's 1 MiB request limit.
    /// </summary>
    public const int DescriptionInputMaxLength = 200_000;

    /// <summary>The most photographs one item has. A piece needs a few angles, not a contact sheet.</summary>
    public const int GalleryMaxCount = 12;

    /// <summary>
    /// The highest starting price staff can set, in cents ($50,000). A guard against a typo creating a Square
    /// object priced in the millions, not a business rule: the gallery's largest canvases run to five figures.
    /// </summary>
    public const long StartingPriceMaxCents = 5_000_000;

    /// <summary>The largest starting quantity: an edition of prints, not a warehouse.</summary>
    public const int StartingStockMaxQuantity = 1_000;

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>The longest category slug stored, so the column is sized once.</summary>
    public const int CategoryMaxLength = 32;

    /// <summary>
    /// The categories artwork is filed under: the set the site already filters on
    /// (<c>projects/site/src/app/pages/shop.ts</c>), and nothing else (ADR 0038). Widening it is a line here, an
    /// ADR note and a label in the admin's <c>SHOP_CATEGORIES</c>.
    /// </summary>
    /// <remarks>Merch is not among them, and never will be: it is POS-only (ADR 0032).</remarks>
    public static IReadOnlyList<ShopCategory> Categories { get; } =
    [
        new("jewelry", "Jewelry"),
        new("prints", "Prints"),
        new("ceramics", "Ceramics"),
        new("painting", "Painting"),
        new("sculpture", "Sculpture"),
        new("books-magazines", "Books and magazines"),
    ];

    /// <summary>Whether <paramref name="slug"/> is one of the categories above.</summary>
    public static bool IsCategory(string? slug) =>
        slug is not null && Categories.Any(category => string.Equals(category.Slug, slug, StringComparison.Ordinal));

    /// <summary>
    /// Where a published item lives on the site: its address is this plus its slug, matching the site's own
    /// <c>shop/:slug</c> route. A slug change after publish redirects the old address (ADR 0035).
    /// </summary>
    public const string SitePathPrefix = SitePaths.Shop;

    /// <summary>Identity's user id column length, so <c>UpdatedBy</c> can hold any user id.</summary>
    internal const int UserIdMaxLength = 450;
}
