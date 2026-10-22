using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Instructors.Facade;

/// <summary>
/// The Instructors module's boundary (ADR 0043): the CMS instructor routes, the site's read routes, Classes' instructor
/// picker and run credits, and the importer (4.x) later. Every write takes the row version as <c>ifMatch</c>.
/// </summary>
public interface IInstructorsFacade
{
    Task<OperationResult<CursorPage<InstructorSummaryServiceModel>>> ListAsync(
        InstructorListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<InstructorServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The Deleted view (ADR 0052): the records staff deleted, Admin only at the route. Its own cursor scope, so a
    /// cursor from the list above is not valid here.
    /// </summary>
    Task<OperationResult<CursorPage<InstructorDeletedServiceModel>>> ListDeletedAsync(
        InstructorListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the instructor from any status (ADR 0052): it leaves the site within the Worker's next pass, leaves
    /// the CMS list, and releases its web address. Admin only at the route (auth.md "Sensitive operations");
    /// audited. Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.in_use</c> while another record still names it.
    /// </summary>
    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings a deleted instructor back as a Draft, with any schedule cleared. Admin only at the route; audited.
    /// Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.slug_taken</c>.
    /// </summary>
    Task<OperationResult<InstructorServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// For another module (Classes' runs): the instructors among <paramref name="ids"/> that exist, whatever their
    /// status, in no particular order. An id with no instructor is simply absent. At most
    /// <see cref="Managers.InstructorPolicy.LookupMaxCount"/> ids.
    /// </summary>
    Task<IReadOnlyList<InstructorSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="GetSummariesAsync"/>, by slug: for the development seed today, and the importer and the site's
    /// instructor pages later. At most <see cref="Managers.InstructorPolicy.LookupMaxCount"/> slugs.
    /// </summary>
    Task<IReadOnlyList<InstructorSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>
    /// Every published instructor, by name, as the site shows them (<c>GET /api/v1/site/instructors</c>). Cached
    /// under the <c>instructor</c> and <c>artist</c> type tags: the artist link's page depends on the artist's status.
    /// </summary>
    Task<OperationResult<IReadOnlyList<SiteInstructorServiceModel>>> ListForSiteAsync(CancellationToken cancellationToken);

    /// <summary>The published instructor at <paramref name="slug"/>, or <c>resource.not_found</c>. Cached as above.</summary>
    Task<OperationResult<SiteInstructorServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>One instructor as the site would show them, whatever the status, for a preview link. Never cached.</summary>
    Task<OperationResult<PreviewRecord<SiteInstructorServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// For another module's site reads (class runs): how each of these instructors is named. An id with no
    /// instructor is simply absent. Any number of ids: a whole list's credits are asked
    /// <see cref="Managers.InstructorPolicy.LookupMaxCount"/> at a time.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel>> DescribeCreditsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every published instructor's address, for <c>/sitemap.xml</c>.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A new Draft. Refusals: <c>validation.failed</c> (including a portrait not in the library, or an artist link that
    /// is not on the roster or is archived), <c>cms.slug_taken</c>.
    /// </summary>
    Task<OperationResult<InstructorServiceModel>> CreateAsync(
        InstructorCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A staff edit. Refusals: <c>validation.failed</c>, <c>resource.not_found</c>, <c>concurrency.*</c>,
    /// <c>cms.slug_taken</c>. A published slug may change: the old address is redirected (ADR 0035).
    /// </summary>
    Task<OperationResult<InstructorServiceModel>> UpdateAsync(
        Guid id, InstructorUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft → Published. Refused while the portrait lacks alt text (<c>validation.failed</c> on <c>portraitMediaId</c>).</summary>
    Task<OperationResult<InstructorServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Published → Draft. The slug stays locked.</summary>
    Task<OperationResult<InstructorServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft or Published → Archived. Admin only at the route (auth.md "Sensitive operations"); audited.</summary>
    Task<OperationResult<InstructorServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled, for the Worker to publish when the time comes (ADR 0036). Refusals:
    /// <c>validation.failed</c> (a time already past, or a portrait that could not be published),
    /// <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.status_transition_invalid</c>.
    /// </summary>
    Task<OperationResult<InstructorServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<InstructorServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher (<see cref="IScheduledPublisher"/>): the instructors due at
    /// <paramref name="asOf"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher: publishes one due instructor, re-read first, claimed by its row version.
    /// Never throws for an instructor that moved on — it reports what happened instead.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);
}
