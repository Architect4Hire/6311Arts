using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// One row of the shop list as Business hands it to the facade: everything in
/// <see cref="ShopItemSummaryServiceModel"/> except who the credited artist is, which only the Artists module can
/// say. The facade asks it once for the page and finishes each row with <see cref="ToServiceModel"/>
/// (backend.md: cross-module reads go facade to facade).
/// </summary>
/// <remarks>
/// A type of its own rather than the ServiceModel with the artist's id hung off it: the id is how this module asks
/// Artists a question, not part of what the API answers. <see cref="ShopItemDetail"/> is the same arrangement for
/// one item.
/// <para>
/// <see cref="ArtistName"/> is the artist's name read through the item's foreign key, as the "artist" sort reads it,
/// so a page resumes where that order left off (ADR 0062). It is never shown; the facade's answer is.
/// </para>
/// </remarks>
internal sealed record ShopItemListRow(
    Guid Id,
    string Title,
    string Slug,
    PublishStatus Status,
    Guid? ArtistId,
    IReadOnlyList<string> Categories,
    Guid? LeadImageMediaId,
    string Sku,
    bool OneOfAKind,
    long? PriceCents,
    int? StockOnHand,
    bool SoldOut,
    DateTimeOffset UpdatedAt,
    string? ArtistName = null)
{
    /// <summary>The finished row, with the credit described by <paramref name="artist"/> when there is one.</summary>
    public ShopItemSummaryServiceModel ToServiceModel(ShopItemArtistServiceModel? artist) => new(
        Id,
        Title,
        Slug,
        Status,
        ArtistId is null ? null : artist,
        Categories,
        LeadImageMediaId,
        Sku,
        OneOfAKind,
        PriceCents,
        StockOnHand,
        SoldOut,
        CatalogPolicy.Currency,
        UpdatedAt);
}
