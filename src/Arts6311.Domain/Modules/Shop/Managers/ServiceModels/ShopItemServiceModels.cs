using System.Text.Json.Serialization;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Shop.Managers.ServiceModels;

/// <summary>One piece of artwork in full, as the CMS edits it.</summary>
/// <param name="Description">Sanitized HTML (ADR 0034): exactly what is stored, which is also all the site will render.</param>
/// <param name="Artist">Who made it, as the Artists module describes them now; <c>null</c> when nobody is credited.</param>
/// <param name="Categories">The slugs it is filed under, from <c>ShopPolicy.Categories</c>.</param>
/// <param name="ImageMediaIds">The photographs, in order. The first is the lead (ADR 0038).</param>
/// <param name="DescriptionImageMediaIds">The library images the description shows.</param>
/// <param name="Sku">CMS-owned, unique across the CMS (ADR 0037).</param>
/// <param name="ItemNumber">The number its suggested SKU is built from.</param>
/// <param name="StartingPriceCents">The CMS's draft price, the value the first Square push creates the item with.</param>
/// <param name="StartingStockQuantity">The CMS's draft count; 1 for a one-of-a-kind piece.</param>
/// <param name="Currency">The currency every amount here is in (api-contract.md).</param>
/// <param name="StartingPriceEditable">
/// Whether <paramref name="StartingPriceCents"/> can still be edited: <c>false</c> once Square has reported money
/// or stock, after which a price change is its own command (ADR 0032, 0037).
/// </param>
/// <param name="StartingStockEditable">
/// Whether <paramref name="OneOfAKind"/> and <paramref name="StartingStockQuantity"/> can still be edited: the same
/// gate, after which quantity moves through the set-quantity command — which one-of-a-kind artwork never gets
/// (ADR 0032, 0038).
/// </param>
/// <param name="PriceCents">Square's record of the price. <c>null</c> until Square has reported one.</param>
/// <param name="StockOnHand">Square's record of the stock. <c>null</c> until the first pull.</param>
/// <param name="SoldOut">Square's record of whether it is sold out.</param>
/// <param name="SquareHasRecord">Whether Square has reported money or stock for this item yet.</param>
/// <param name="SlugLocked">Whether it has ever been published, so that changing its slug redirects the old address (ADR 0035).</param>
/// <param name="LegacyWordPressId">The WooCommerce product it was imported from, for provenance.</param>
/// <param name="UpdatedBy">The user id behind the last write.</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
public sealed record ShopItemServiceModel(
    Guid Id,
    string Title,
    string Slug,
    string? Description,
    ShopItemArtistServiceModel? Artist,
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
    bool SlugLocked,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>The artist a shop item credits, as the Artists module describes them now.</summary>
/// <param name="Status">The artist's own status: work credited to an archived or draft artist is shown as such.</param>
public sealed record ShopItemArtistServiceModel(Guid Id, string Name, string Slug, PublishStatus Status);

/// <summary>
/// One row of the shop list. It carries what Square says about money and stock, because that is what staff scan
/// the list for.
/// </summary>
/// <param name="Artist">Filled in by the facade from the Artists module; <c>null</c> when nobody is credited.</param>
/// <param name="LeadImageMediaId">The first photograph, for the row's thumbnail.</param>
/// <param name="Currency">The currency <paramref name="PriceCents"/> is in (api-contract.md).</param>
public sealed record ShopItemSummaryServiceModel(
    Guid Id,
    string Title,
    string Slug,
    PublishStatus Status,
    ShopItemArtistServiceModel? Artist,
    IReadOnlyList<string> Categories,
    Guid? LeadImageMediaId,
    string Sku,
    bool OneOfAKind,
    long? PriceCents,
    int? StockOnHand,
    bool SoldOut,
    string Currency,
    DateTimeOffset UpdatedAt);
