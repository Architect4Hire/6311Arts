using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Shop.Facade;

/// <summary>
/// The Shop module's boundary: the CMS shop routes today; the site's read routes (Phase 3), the Square push (5.x)
/// and the importer (4.x) later. Every write takes the row version as <c>ifMatch</c>.
/// </summary>
public interface IShopFacade
{
    Task<OperationResult<CursorPage<ShopItemSummaryServiceModel>>> ListAsync(
        ShopItemListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ShopItemServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The Deleted view (ADR 0052): the records staff deleted, Admin only at the route. Its own cursor scope, so a
    /// cursor from the list above is not valid here.
    /// </summary>
    Task<OperationResult<CursorPage<ShopItemDeletedServiceModel>>> ListDeletedAsync(
        ShopItemListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the piece from any status (ADR 0052): it leaves the site within the Worker's next pass, leaves
    /// the CMS list, and releases its web address. Admin only at the route (auth.md "Sensitive operations");
    /// audited. Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>.
    /// </summary>
    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings a deleted piece back as a Draft, with any schedule cleared. Admin only at the route; audited.
    /// Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.slug_taken</c>, <c>cms.sku_taken</c>.
    /// </summary>
    Task<OperationResult<ShopItemServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A new Draft. Refusals: <c>validation.failed</c> (including a photograph not in the library, or an artist not
    /// on the roster or archived), <c>cms.slug_taken</c>, <c>cms.sku_taken</c>.
    /// </summary>
    Task<OperationResult<ShopItemServiceModel>> CreateAsync(
        ShopItemCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A staff edit. Refusals: <c>validation.failed</c>, <c>resource.not_found</c>, <c>concurrency.*</c>,
    /// <c>cms.slug_taken</c>, <c>cms.sku_taken</c>. A published slug may change: the old address is redirected
    /// (ADR 0035).
    /// </summary>
    Task<OperationResult<ShopItemServiceModel>> UpdateAsync(
        Guid id, ShopItemUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft → Published. Refused (<c>validation.failed</c>) without a description, a price, a starting count or a
    /// photograph, or while a photograph lacks alt text.
    /// </summary>
    Task<OperationResult<ShopItemServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Published → Draft. The slug stays as it is.</summary>
    Task<OperationResult<ShopItemServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft or Published → Archived. Admin only at the route (auth.md "Sensitive operations"); audited.</summary>
    Task<OperationResult<ShopItemServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled, for the Worker to publish when the time comes (ADR 0036). Refusals:
    /// <c>validation.failed</c> (a time already past, or anything publishing would refuse),
    /// <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.status_transition_invalid</c>.
    /// </summary>
    Task<OperationResult<ShopItemServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<ShopItemServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher (<see cref="IScheduledPublisher"/>): the items due at
    /// <paramref name="asOf"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher: publishes one due item, re-read first, claimed by its row version.
    /// Never throws for a record that moved on — it reports what happened instead.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /site/shop</c>: one page of published artwork, cursor-paged, with how many works the filters match in
    /// all (ADR 0042). A first page is cached under the <c>shop-item</c> and <c>artist</c> type tags; a later page,
    /// whose key would be the visitor's cursor, is read each time. Refusals: <c>validation.failed</c>,
    /// <c>paging.cursor.invalid</c>.
    /// </summary>
    Task<OperationResult<CountedCursorPage<SiteShopItemSummaryServiceModel>>> ListForSiteAsync(
        SiteShopListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /site/shop/{slug}</c>: the published piece there, cached as a first page is. Anything else is
    /// <c>resource.not_found</c>, and is not cached.
    /// </summary>
    Task<OperationResult<SiteShopItemServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>For a preview link: the piece as the site would show it, whatever the status. Never cached.</summary>
    Task<OperationResult<PreviewRecord<SiteShopItemServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// For the Site module's artist page: the first page of <c>/site/shop?artist={artistSlug}</c>, newest first,
    /// cached as that list is. Its next cursor continues on that route.
    /// </summary>
    Task<CursorPage<SiteShopItemSummaryServiceModel>> ListWorksForSiteAsync(string artistSlug, CancellationToken cancellationToken);

    /// <summary>
    /// For the Site module's artist directory: how many published pieces each artist has. Not cached here; the
    /// Site module caches the directory.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountForSiteByArtistAsync(CancellationToken cancellationToken);

    /// <summary>For the sitemap. Not cached here; the Site module caches the sitemap.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);
}
