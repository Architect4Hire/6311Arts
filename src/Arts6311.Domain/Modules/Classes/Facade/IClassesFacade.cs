using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Classes.Facade;

/// <summary>
/// The Classes module's boundary: the CMS class routes and the site's read routes; the Square push (5.x) and the
/// importer (4.x) later. Every write takes the row version as <c>ifMatch</c>.
/// </summary>
public interface IClassesFacade
{
    Task<OperationResult<CursorPage<ClassSummaryServiceModel>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ClassServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The Deleted view (ADR 0052): the records staff deleted, Admin only at the route. Its own cursor scope, so a
    /// cursor from the list above is not valid here.
    /// </summary>
    Task<OperationResult<CursorPage<ClassDeletedServiceModel>>> ListDeletedAsync(
        ClassListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the class from any status (ADR 0052): it leaves the site within the Worker's next pass, leaves
    /// the CMS list, and releases its web address. Admin only at the route (auth.md "Sensitive operations");
    /// audited. Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>.
    /// </summary>
    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings a deleted class back as a Draft, with any schedule cleared. Admin only at the route; audited.
    /// Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.slug_taken</c>, <c>cms.sku_taken</c>.
    /// </summary>
    Task<OperationResult<ClassServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A new Draft. Refusals: <c>validation.failed</c> (including a hero or body picture not in the library, or a run's
    /// instructor who is not there or is archived), <c>cms.slug_taken</c>, <c>cms.sku_taken</c>.
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
    /// Draft → Published. Refused (<c>validation.failed</c>) without a summary or a run, while a run lacks an
    /// instructor, a session or a starting price, or while the hero lacks alt text.
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

    /// <summary>
    /// <c>GET /site/classes</c>: every published class with a run still to come, by its next run, cached under the
    /// <c>class-offering</c> and <c>instructor</c> type tags (ADR 0042, 0044). Only what is upcoming, so not paged.
    /// Given an instructor's slug, only the runs that published instructor teaches; any other slug is an empty list.
    /// </summary>
    Task<OperationResult<IReadOnlyList<SiteClassSummaryServiceModel>>> ListForSiteAsync(
        SiteClassListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /site/classes/{slug}</c>: the published class there, cached as the list is. Anything else is
    /// <c>resource.not_found</c>, and is not cached.
    /// </summary>
    Task<OperationResult<SiteClassServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>For a preview link: the class as the site would show it, whatever the status. Never cached.</summary>
    Task<OperationResult<PreviewRecord<SiteClassServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>For the sitemap. Not cached here; the Site module caches the sitemap.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);
}
