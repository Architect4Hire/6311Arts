using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers;

namespace Arts6311.Domain.Modules.Shop.Data.Repositories;

internal interface IShopRepository
{
    void Add(ShopItem item);

    /// <summary>One item with its categories, gallery and description images, tracked.</summary>
    Task<ShopItem?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another item than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use. For the development seed, which adds only the items that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The number a new item takes: one past the highest in use, for its suggested SKU (ADR 0037). A suggestion
    /// aid, so a race that hands two items the same number costs nothing.
    /// </summary>
    Task<int> NextItemNumberAsync(CancellationToken cancellationToken);

    /// <summary>One page in (Title, Id) order, projected straight to list rows.</summary>
    Task<List<ShopItemListRow>> ListAsync(
        ShopItemListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        string? category,
        CancellationToken cancellationToken);

    /// <summary>
    /// The ids of items Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// One page of published items in <paramref name="query"/>'s order, after its keyset, projected straight to site
    /// rows (ADR 0042).
    /// </summary>
    Task<List<ShopItemSiteRow>> ListPublishedAsync(ShopSiteQuery query, CancellationToken cancellationToken);

    /// <summary>The published item at <paramref name="slug"/>, projected straight to a site row.</summary>
    Task<ShopItemSiteRow?> FindPublishedAsync(string slug, CancellationToken cancellationToken);

    /// <summary>How many published items each credited artist has. An artist with none is absent.</summary>
    Task<Dictionary<Guid, int>> CountPublishedByArtistAsync(CancellationToken cancellationToken);

    /// <summary>Every published item's slug and last change, by slug, for the sitemap. No descriptions.</summary>
    Task<List<(string Slug, DateTimeOffset UpdatedAt)>> ListPublishedAddressesAsync(CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
