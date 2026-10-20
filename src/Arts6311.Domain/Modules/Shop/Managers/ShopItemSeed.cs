namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>One development shop item, as the seeder hands it to Business: the artist already resolved to an id.</summary>
/// <param name="StartingPriceCents">What it costs.</param>
/// <param name="StartingStockQuantity">How many there are; 1 when <paramref name="OneOfAKind"/>.</param>
internal sealed record ShopItemSeed(
    string Title,
    string Description,
    Guid? ArtistId,
    IReadOnlyList<string> Categories,
    bool OneOfAKind,
    long StartingPriceCents,
    int StartingStockQuantity);
