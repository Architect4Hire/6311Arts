using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// One shop item as Business hands it to the facade: everything in <see cref="ShopItemServiceModel"/> except who
/// the credited artist is, which only the Artists module can say. The facade asks it and finishes the model with
/// <see cref="ToServiceModel"/> (backend.md: cross-module reads go facade to facade).
/// </summary>
internal sealed record ShopItemDetail(
    Guid Id,
    string Title,
    string Slug,
    string? Description,
    Guid? ArtistId,
    IReadOnlyList<string> Categories,
    IReadOnlyList<Guid> ImageMediaIds,
    IReadOnlyList<Guid> DescriptionImageMediaIds,
    string Sku,
    int ItemNumber,
    bool OneOfAKind,
    long? StartingPriceCents,
    int? StartingStockQuantity,
    string Currency,
    bool StartingPriceEditable,
    bool StartingStockEditable,
    long? PriceCents,
    int? StockOnHand,
    bool SoldOut,
    bool SquareHasRecord,
    PublishStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledFor,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    string ETag)
{
    /// <summary>
    /// The finished model, with the credit described by <paramref name="artist"/>. A credit Artists did not
    /// describe is left out; the <c>Restrict</c> foreign key means that cannot happen today.
    /// </summary>
    public ShopItemServiceModel ToServiceModel(ShopItemArtistServiceModel? artist) => new(
        Id,
        Title,
        Slug,
        Description,
        ArtistId is null ? null : artist,
        Categories,
        ImageMediaIds,
        DescriptionImageMediaIds,
        Sku,
        ItemNumber,
        OneOfAKind,
        StartingPriceCents,
        StartingStockQuantity,
        Currency,
        StartingPriceEditable,
        StartingStockEditable,
        PriceCents,
        StockOnHand,
        SoldOut,
        SquareHasRecord,
        Status,
        PublishedAt,
        ScheduledFor,
        SlugLocked: PublishedAt is not null,
        LegacyWordPressId,
        CreatedAt,
        UpdatedAt,
        UpdatedBy,
        ETag);
}
