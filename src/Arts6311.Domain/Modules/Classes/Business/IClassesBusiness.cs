using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Classes.Business;

/// <summary>
/// The class rules: slugs unique, a body that is only ever the sanitizer's output, runs that each have their own SKU
/// across the whole CMS, and Square's record of money and seats never written from here (ADR 0032, 0037, 0044).
/// </summary>
/// <remarks>
/// It returns <see cref="ClassDetail"/> rather than the finished ServiceModel, as Shop does: each run names an
/// instructor, whom only the Instructors module can describe, so the facade completes the answer.
/// </remarks>
internal interface IClassesBusiness
{
    Task<OperationResult<CursorPage<ClassListRow>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ClassDetail>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The Deleted view: the records staff deleted, on this list's own cursor scope (ADR 0052).</summary>
    Task<OperationResult<CursorPage<ClassDeletedServiceModel>>> ListDeletedAsync(
        ClassListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the class, whatever its status: it leaves the site, leaves the CMS list and releases its web
    /// address, and its SKU, and only the Deleted view and the audit trail still name it (ADR 0052). Refusals:
    /// <c>resource.not_found</c>, <c>concurrency.*</c>.
    /// </summary>
    Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings a deleted class back as a Draft, with any schedule cleared: nothing returns to the site by being
    /// restored (ADR 0052). Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.slug_taken</c>, <c>cms.sku_taken</c>.
    /// </summary>
    Task<OperationResult<ClassDetail>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <param name="media">What Media said about the hero and about the body's images.</param>
    /// <param name="instructors">What Instructors said about the instructors the runs name.</param>
    Task<OperationResult<ClassDetail>> CreateAsync(
        ClassCreateViewModel create,
        ClassMediaCheck media,
        ClassInstructorCheck instructors,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <param name="media">What Media said about a hero or a body sent with this edit.</param>
    /// <param name="instructors">What Instructors said about the instructors the runs name, when runs are sent.</param>
    Task<OperationResult<ClassDetail>> UpdateAsync(
        Guid id,
        ClassUpdateViewModel update,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <param name="media">Required for <see cref="PublishCommand.Publish"/>: what Media said about the hero.</param>
    /// <param name="instructors">
    /// Required for <see cref="PublishCommand.Publish"/>: which of the instructors the runs name still exist. A
    /// run may name one that has since been deleted (ADR 0052), and publishing needs a real instructor.
    /// </param>
    Task<OperationResult<ClassDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled for <paramref name="scheduledFor"/>. Refusals: <c>concurrency.*</c>,
    /// <c>cms.status_transition_invalid</c>, and <c>validation.failed</c> for a time already past or anything
    /// publishing would refuse (ADR 0036). Its own method, because <c>schedule</c> is the one command with a body.
    /// </summary>
    Task<OperationResult<ClassDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ClassMediaCheck? media,
        ClassInstructorCheck? instructors,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<ClassDetail>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The offerings due to be published at <paramref name="asOf"/>, for the Worker's scheduled publisher.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Every published class with a run still to come, by its next run, with only the runs that have not ended
    /// (ADR 0042, 0044) — and, given <paramref name="instructorId"/>, only the runs that instructor teaches.
    /// </summary>
    Task<IReadOnlyList<ClassSiteRow>> ListForSiteAsync(Guid? instructorId, CancellationToken cancellationToken);

    /// <summary>
    /// The published class at <paramref name="slug"/>, or <c>null</c>: a draft is not there to the site. A class
    /// whose runs are all over is still there, with none listed.
    /// </summary>
    Task<ClassSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>One class as the site would show it, whatever the status, for a preview link.</summary>
    Task<OperationResult<PreviewRecord<ClassSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every published class's address and last change, for the sitemap, past ones included.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one due offering on the scheduled publisher's behalf, re-reading it first: the row version is the
    /// claim, so two Workers acting on one record produce one publish and one
    /// <see cref="ScheduledPublishOutcome.Lost"/>.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ClassMediaCheck? media, ClassInstructorCheck? instructors, CancellationToken cancellationToken);

    /// <summary>
    /// The development classes: adds, as Published (or as a Draft when a run has no instructor) and without heroes,
    /// each one whose slug is not in use yet, with
    /// a suggested SKU per run. Returns how many were added. Never touches an existing row, so a staff edit survives
    /// a rerun.
    /// </summary>
    Task<int> SeedAsync(IReadOnlyList<ClassSeed> seeds, string actor, CancellationToken cancellationToken);
}
