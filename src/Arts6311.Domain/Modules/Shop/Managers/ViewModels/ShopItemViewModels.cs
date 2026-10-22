using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Shop.Managers.ViewModels;

/// <summary>
/// A new piece of artwork for the shop. It starts as a Draft; publishing is its own command.
/// </summary>
/// <remarks>
/// No price, stock or sold-out field appears on this model or any other write model in this module: those are
/// Square's record, kept as read-only projections (square.md, ADR 0032). <c>StartingPriceCents</c> and
/// <c>StartingStockQuantity</c> are the CMS's own draft fields, the values the first Square push creates the item
/// with.
/// </remarks>
/// <param name="Slug">Optional: made from <paramref name="Title"/> when left out.</param>
/// <param name="Description">HTML. It is sanitized before it is stored (ADR 0034); what comes back is what was kept.</param>
/// <param name="ArtistId">Who made it: an artist on the roster that is not archived. Optional.</param>
/// <param name="Categories">Slugs from <c>ShopPolicy.Categories</c> (ADR 0038).</param>
/// <param name="ImageMediaIds">The photographs, in order. The first is the lead.</param>
/// <param name="Sku">Optional: the next free suggestion (<c>ART-&lt;item&gt;</c>) when left out.</param>
/// <param name="OneOfAKind"><c>true</c> forces <paramref name="StartingStockQuantity"/> to 1 (ADR 0032).</param>
public sealed record ShopItemCreateViewModel(
    string Title,
    string? Slug = null,
    string? Description = null,
    Guid? ArtistId = null,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<Guid>? ImageMediaIds = null,
    string? Sku = null,
    bool? OneOfAKind = null,
    long? StartingPriceCents = null,
    int? StartingStockQuantity = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged; an empty string clears the description. Status is
/// not here: it changes only through the publish, unpublish, schedule and archive commands (content.md).
/// </summary>
/// <param name="Slug">Once the item has been published, changing it redirects the old address to the new one (ADR 0035).</param>
/// <param name="RemoveArtist"><c>true</c> takes the credit off; not allowed together with <paramref name="ArtistId"/>.</param>
/// <param name="Categories">Replaces the categories; an empty list clears them.</param>
/// <param name="ImageMediaIds">Replaces the gallery, in order; an empty list clears it.</param>
/// <param name="OneOfAKind">Refused once Square holds the record (ADR 0038).</param>
/// <param name="StartingPriceCents">Refused once Square has reported money or stock: from then on it is a change-price command (5.4a).</param>
/// <param name="RemoveStartingPrice"><c>true</c> clears it; not allowed together with <paramref name="StartingPriceCents"/>.</param>
/// <param name="StartingStockQuantity">Refused once Square holds the record: from then on it is a set-quantity command (5.4b).</param>
public sealed record ShopItemUpdateViewModel(
    string? Title = null,
    string? Slug = null,
    string? Description = null,
    Guid? ArtistId = null,
    bool? RemoveArtist = null,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<Guid>? ImageMediaIds = null,
    string? Sku = null,
    bool? OneOfAKind = null,
    long? StartingPriceCents = null,
    bool? RemoveStartingPrice = null,
    int? StartingStockQuantity = null);

/// <summary>
/// A spreadsheet of new pieces, as CSV text (ADR 0053): each row becomes a Draft, all of them or none. The columns
/// are <c>ShopImportColumns</c>; the admin offers a template.
/// </summary>
/// <remarks>Text in a JSON body rather than a file upload: the admin reads the file, and this binds like any other model.</remarks>
/// <param name="Csv">The whole file, decoded as UTF-8. A header row, then up to <c>ShopPolicy.ImportMaxRows</c> rows.</param>
/// <param name="FileName">The file's name, for the audit trail only.</param>
public sealed record ShopImportViewModel(string Csv, string? FileName = null);

/// <summary>A page of shop items, by title.</summary>
/// <param name="Search">Part of a title, matched without regard to case.</param>
/// <param name="Category">One category slug, to show only what is filed under it.</param>
public sealed record ShopItemListViewModel(
    string? Cursor = null,
    int? Limit = null,
    PublishStatus? Status = null,
    string? Search = null,
    string? Category = null);

/// <summary>A page of the site's shop (ADR 0042): published artwork only, filtered and ordered as the visitor chose.</summary>
/// <param name="Category">One category slug, to show only what is filed under it.</param>
/// <param name="Artist">An artist's slug, to show only their work. An artist who is not published shows nothing.</param>
/// <param name="Sort"><c>newest</c> (the default), <c>price-asc</c>, <c>price-desc</c> or <c>title</c>.</param>
public sealed record SiteShopListViewModel(
    string? Category = null,
    string? Artist = null,
    string? Sort = null,
    string? Cursor = null,
    int? Limit = null);
