using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Classes.Facade;

/// <summary>
/// The Classes module's boundary: the CMS class routes today; the site's read routes (Phase 3), the Square push
/// (5.x) and the importer (4.x) later. Every write takes the row version as <c>ifMatch</c>.
/// </summary>
public interface IClassesFacade
{
    Task<OperationResult<CursorPage<ClassSummaryServiceModel>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ClassServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// A new Draft. Refusals: <c>validation.failed</c> (including a hero or body picture not in the library),
    /// <c>cms.slug_taken</c>, <c>cms.sku_taken</c>.
    /// </summary>
    Task<OperationResult<ClassServiceModel>> CreateAsync(
        ClassCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A staff edit. Refusals: <c>validation.failed</c>, <c>resource.not_found</c>, <c>concurrency.*</c>,
    /// <c>cms.slug_taken</c>, <c>cms.sku_taken</c>. A published slug may change: the old address is redirected
    /// (ADR 0035).
    /// </summary>
    Task<OperationResult<ClassServiceModel>> UpdateAsync(
        Guid id, ClassUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft → Published. Refused (<c>validation.failed</c>) without a summary, a starting price or a session, or
    /// while the hero lacks alt text.
    /// </summary>
    Task<OperationResult<ClassServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Published → Draft. The slug stays as it is.</summary>
    Task<OperationResult<ClassServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft or Published → Archived. Admin only at the route (auth.md "Sensitive operations"); audited.</summary>
    Task<OperationResult<ClassServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled, for the Worker to publish when the time comes (ADR 0036). Refusals:
    /// <c>validation.failed</c> (a time already past, or anything publishing would refuse),
    /// <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.status_transition_invalid</c>.
    /// </summary>
    Task<OperationResult<ClassServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<ClassServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher (<see cref="IScheduledPublisher"/>): the offerings due at
    /// <paramref name="asOf"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher: publishes one due offering, re-read first, claimed by its row version.
    /// Never throws for a record that moved on — it reports what happened instead.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);
}
