using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Instructors.Business;

internal interface IInstructorsBusiness
{
    Task<OperationResult<CursorPage<InstructorSummaryServiceModel>>> ListAsync(
        InstructorListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<InstructorDetail>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The instructors among <paramref name="ids"/> that exist. Empty in, empty out, without a query.</summary>
    Task<IReadOnlyList<InstructorSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The instructors among <paramref name="slugs"/> that exist. Empty in, empty out, without a query.</summary>
    Task<IReadOnlyList<InstructorSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>Every published instructor, by name, for the site (ADR 0042).</summary>
    Task<IReadOnlyList<InstructorSiteRow>> ListForSiteAsync(CancellationToken cancellationToken);

    /// <summary>The published instructor at <paramref name="slug"/>, or <c>null</c>: a draft is not there to the site.</summary>
    Task<InstructorSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>One instructor as the site would show them, whatever the status, for a preview link.</summary>
    Task<OperationResult<PreviewRecord<InstructorSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// How a class run names each of <paramref name="ids"/>: the name always, the page only when the instructor is
    /// published.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel>> DescribeCreditsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every published instructor's address and last change, for the sitemap.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);

    Task<OperationResult<InstructorDetail>> CreateAsync(
        InstructorCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <param name="portrait">What Media said about <c>update.PortraitMediaId</c>, when one is sent.</param>
    Task<OperationResult<InstructorDetail>> UpdateAsync(
        Guid id,
        InstructorUpdateViewModel update,
        string? ifMatch,
        InstructorPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The development roster: adds, as Published and without portraits, each instructor whose slug is not in use yet.
    /// Returns how many were added. Never touches an existing row, so a staff edit survives a rerun.
    /// </summary>
    Task<int> SeedAsync(IReadOnlyList<InstructorSeed> roster, string actor, CancellationToken cancellationToken);

    /// <param name="portrait">Required for <see cref="PublishCommand.Publish"/>: what Media said about the portrait.</param>
    Task<OperationResult<InstructorDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        InstructorPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled for <paramref name="scheduledFor"/>. Refusals: <c>concurrency.*</c>,
    /// <c>cms.status_transition_invalid</c>, and <c>validation.failed</c> for a time already past or a portrait
    /// that could not be published (ADR 0036). The <c>schedule</c> command is its own method rather than another
    /// <see cref="TransitionAsync"/> argument, because it is the one command with a body.
    /// </summary>
    /// <param name="portrait">What Media said about the instructor's current portrait, as for a publish.</param>
    Task<OperationResult<InstructorDetail>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        InstructorPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<InstructorDetail>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The instructors due to be published at <paramref name="asOf"/>, for the Worker's scheduled publisher.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one due instructor on the scheduled publisher's behalf, re-reading it first: the row version is the
    /// claim, so two Workers acting on one instructor produce one publish and one <see cref="ScheduledPublishOutcome.Lost"/>.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, InstructorPortraitCheck? portrait, CancellationToken cancellationToken);
}
