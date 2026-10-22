using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers;

namespace Arts6311.Domain.Modules.Shop.Data;

/// <summary>Where a page of shop items resumes: after this row in (Title, Id) order.</summary>
internal sealed record ShopItemListPosition(string Title, Guid Id);

/// <summary>The orders the site's shop offers (<c>ShopPolicy.SiteSorts</c>, ADR 0042).</summary>
internal enum ShopSiteSort
{
    /// <summary>First publish, latest first, then id descending.</summary>
    Newest = 0,

    /// <summary>Square's price, lowest first, unpriced last, then id.</summary>
    PriceAscending = 10,

    /// <summary>Square's price, highest first, unpriced last, then id.</summary>
    PriceDescending = 20,

    /// <summary>Title, then id.</summary>
    Title = 30,
}

/// <summary>
/// One page of the site's shop: which published items, in which order, after which row. Only the field
/// <see cref="Sort"/> orders by is set in <see cref="After"/>'s keyset.
/// </summary>
internal sealed record ShopSiteQuery(string? Category, Guid? ArtistId, ShopSiteSort Sort, ShopSitePosition? After, int Take);

/// <summary>Where a page of the site's shop resumes: after the row with these values in the query's order.</summary>
internal sealed record ShopSitePosition(Guid Id, DateTimeOffset? PublishedAt = null, long? PriceCents = null, string? Title = null);

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

    /// <summary>
    /// What to call the live records this module holds that name artist <paramref name="artistId"/>, at most
    /// <paramref name="take"/> of them: the answer this module's <c>IContentReferenceSource</c> gives the kernel
    /// before that artist is deleted (ADR 0052).
    /// </summary>
    Task<List<string>> ListNamingArtistAsync(Guid artistId, int take, CancellationToken cancellationToken);

    /// <summary>One deleted piece, tracked, for a restore (ADR 0052). A live record is <c>null</c> here.</summary>
    Task<ShopItem?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of the Deleted view, in the same order and on the same cursor as the list.</summary>
    Task<List<ShopItemDeletedRow>> ListDeletedAsync(
        ShopItemListPosition? after, int take, string? search, CancellationToken cancellationToken);

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
    /// Marks the piece deleted — who and when, a new row version, its audit entry, the SKUs it lets go and,
    /// when the site was showing it, <c>site.revalidate-requested</c> — in one save (ADR 0052).
    /// </summary>
    /// <param name="skuChanges">The SKUs this record lets go, which Business read off it: deleting frees them for
    /// the record that replaces it (ADR 0037, 0052).</param>
    Task<ShopWriteOutcome> DeleteAsync(ShopItem item, SkuChanges skuChanges, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a restore Business already applied (back to Draft), clearing the delete marks, with its audit entry,
    /// in one save (ADR 0052).
    /// </summary>
    /// <param name="deletedAs">What the record was when it was deleted, for the audit line.</param>
    /// <param name="skuChanges">The SKUs this record takes back, which Business read off it.</param>
    Task<ShopWriteOutcome> RestoreAsync(
        ShopItem item, SkuChanges skuChanges, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the scheduled publisher giving up: the Draft that Business already applied, its revalidation, and an
    /// audit row of its own saying why (ADR 0036).
    /// </summary>
    Task<ShopWriteOutcome> AbandonScheduleAsync(
        ShopItem item, PublishStatus from, string reason, CancellationToken cancellationToken);

    /// <summary>The ids of items Scheduled for <paramref name="asOf"/> or earlier, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>One page of published items for the site, as site rows.</summary>
    Task<List<ShopItemSiteRow>> ListPublishedAsync(ShopSiteQuery query, CancellationToken cancellationToken);

    /// <summary>How many published items the site's filters match, for the shop's count.</summary>
    Task<int> CountPublishedAsync(string? category, Guid? artistId, CancellationToken cancellationToken);

    /// <summary>The published item at <paramref name="slug"/>, as a site row.</summary>
    Task<ShopItemSiteRow?> FindPublishedAsync(string slug, CancellationToken cancellationToken);

    /// <summary>How many published items each credited artist has.</summary>
    Task<Dictionary<Guid, int>> CountPublishedByArtistAsync(CancellationToken cancellationToken);

    /// <summary>Every published item's slug and last change, for the sitemap.</summary>
    Task<List<(string Slug, DateTimeOffset UpdatedAt)>> ListPublishedAddressesAsync(CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the development seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the seed items Business chose, with their SKUs and one audit entry, in one save.</summary>
    Task<int> AddSeedAsync(
        IReadOnlyList<ShopItem> items, SkuChanges skus, string actor, CancellationToken cancellationToken);
}
