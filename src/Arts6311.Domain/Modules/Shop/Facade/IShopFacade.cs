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
}
