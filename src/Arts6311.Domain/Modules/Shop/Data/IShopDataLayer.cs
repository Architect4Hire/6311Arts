using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers;

namespace Arts6311.Domain.Modules.Shop.Data;

/// <summary>Where a page of shop items resumes: after this row in (Title, Id) order.</summary>
internal sealed record ShopItemListPosition(string Title, Guid Id);

internal enum ShopWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another item took the slug between Business's check and the save.</summary>
    SlugTaken = 20,

    /// <summary>Another record took the SKU between Business's check and the save (ADR 0037).</summary>
    SkuTaken = 30,
}

internal interface IShopDataLayer
{
    /// <summary>One item with its categories, gallery and description images, tracked.</summary>
    Task<ShopItem?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Who holds each of <paramref name="skus"/> now, so Business can refuse one another record has.</summary>
    Task<IReadOnlyDictionary<string, SkuOwner>> SkuOwnersAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken);

    /// <summary>The number a new item takes, for its suggested SKU (ADR 0037).</summary>
    Task<int> NextItemNumberAsync(CancellationToken cancellationToken);

    Task<List<ShopItemListRow>> ListAsync(
        ShopItemListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        string? category,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds the item Business built — categories, gallery, description images, the SKU reservation, bookkeeping and
    /// audit entry — in one save.
    /// </summary>
    Task<ShopWriteOutcome> CreateAsync(
        ShopItem item, SkuChanges skus, string actorUserId, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="item"/>, with its SKU reservation and release and its
    /// audit entry, in one save — and, when <paramref name="pathChange"/> is set, <c>redirects.slug-changed</c>
    /// staged before it (ADR 0035).
    /// </summary>
    Task<ShopWriteOutcome> UpdateAsync(
        ShopItem item,
        SkuChanges skus,
        string actorUserId,
        string summary,
        PublishedPathChange? pathChange,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves a status change Business already applied, with its audit entry and <c>site.revalidate-requested</c>,
    /// in one save (ADR 0031, 0036).
    /// </summary>
    Task<ShopWriteOutcome> TransitionAsync(
        ShopItem item, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the scheduled publisher giving up: the Draft that Business already applied, its revalidation, and an
    /// audit row of its own saying why (ADR 0036).
    /// </summary>
    Task<ShopWriteOutcome> AbandonScheduleAsync(
        ShopItem item, PublishStatus from, string reason, CancellationToken cancellationToken);

    /// <summary>The ids of items Scheduled for <paramref name="asOf"/> or earlier, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the development seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the seed items Business chose, with their SKUs and one audit entry, in one save.</summary>
    Task<int> AddSeedAsync(
        IReadOnlyList<ShopItem> items, SkuChanges skus, string actor, CancellationToken cancellationToken);
}
