namespace Arts6311.Domain.Modules.Shop.Managers.ServiceModels;

/// <summary>
/// What an import would create (ADR 0053): every row as it was read, with what is wrong with it. Nothing is written;
/// the import itself runs only when <paramref name="ErrorCount"/> is zero.
/// </summary>
/// <param name="RowCount">How many pieces the file describes. Blank lines are not counted.</param>
/// <param name="ErrorCount">How many of them need fixing first.</param>
public sealed record ShopImportPreviewServiceModel(
    int RowCount,
    int ErrorCount,
    IReadOnlyList<ShopImportRowServiceModel> Rows);

/// <summary>One row of an import, read.</summary>
/// <param name="Row">Its row number in the spreadsheet: the header is row 1, so the first piece is row 2.</param>
/// <param name="Slug">The web address it would take: the one given, or one made from the title.</param>
/// <param name="Sku">The SKU it would take: the one given, normalized, or the next free suggestion.</param>
/// <param name="SkuSuggested">Whether <paramref name="Sku"/> is a suggestion rather than one the file gave.</param>
/// <param name="Artist">The artist the <c>artist</c> column named, when it named exactly one.</param>
/// <param name="Categories">Category slugs, from the slugs or labels the file gave.</param>
/// <param name="Errors">What must be fixed in the file before it can be imported; empty when the row is ready.</param>
public sealed record ShopImportRowServiceModel(
    int Row,
    string Title,
    string Slug,
    string Sku,
    bool SkuSuggested,
    ShopItemArtistServiceModel? Artist,
    IReadOnlyList<string> Categories,
    bool OneOfAKind,
    long? StartingPriceCents,
    int? StartingStockQuantity,
    IReadOnlyList<ShopImportProblemServiceModel> Errors);

/// <summary>One thing wrong with one row.</summary>
/// <param name="Field">The CSV column it is about (<c>sku</c>, <c>starting_price</c>), or <c>row</c> for the whole row.</param>
public sealed record ShopImportProblemServiceModel(string Field, string Message);

/// <summary>What an import created: every row of the file, each now a Draft.</summary>
public sealed record ShopImportResultServiceModel(
    int CreatedCount,
    IReadOnlyList<ShopImportCreatedServiceModel> Items);

/// <summary>One piece an import created, and the row it came from.</summary>
public sealed record ShopImportCreatedServiceModel(int Row, Guid Id, string Title, string Slug, string Sku);
