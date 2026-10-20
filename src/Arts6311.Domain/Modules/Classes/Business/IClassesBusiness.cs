using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Classes.Business;

/// <summary>
/// The class rules: slugs unique, a body that is only ever the sanitizer's output, sessions that each have their own
/// SKU across the whole CMS, and Square's record of money and seats never written from here (ADR 0032, 0037).
/// </summary>
/// <remarks>
/// It returns the finished ServiceModel, as Pages does: nothing on an offering belongs to another module — the
/// instructor is a name, not a link to the artist roster — so there is nothing for the facade to complete.
/// </remarks>
internal interface IClassesBusiness
{
    Task<OperationResult<CursorPage<ClassSummaryServiceModel>>> ListAsync(
        ClassListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ClassServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <param name="media">What Media said about the hero and about the body's images.</param>
    Task<OperationResult<ClassServiceModel>> CreateAsync(
        ClassCreateViewModel create, ClassMediaCheck media, string actorUserId, CancellationToken cancellationToken);

    /// <param name="media">What Media said about a hero or a body sent with this edit.</param>
    Task<OperationResult<ClassServiceModel>> UpdateAsync(
        Guid id,
        ClassUpdateViewModel update,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <param name="media">Required for <see cref="PublishCommand.Publish"/>: what Media said about the hero.</param>
    Task<OperationResult<ClassServiceModel>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled for <paramref name="scheduledFor"/>. Refusals: <c>concurrency.*</c>,
    /// <c>cms.status_transition_invalid</c>, and <c>validation.failed</c> for a time already past or anything
    /// publishing would refuse (ADR 0036). Its own method, because <c>schedule</c> is the one command with a body.
    /// </summary>
    Task<OperationResult<ClassServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ClassMediaCheck? media,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<ClassServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The offerings due to be published at <paramref name="asOf"/>, for the Worker's scheduled publisher.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one due offering on the scheduled publisher's behalf, re-reading it first: the row version is the
    /// claim, so two Workers acting on one record produce one publish and one
    /// <see cref="ScheduledPublishOutcome.Lost"/>.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ClassMediaCheck? media, CancellationToken cancellationToken);

    /// <summary>
    /// The development classes: adds, as Published and without heroes, each one whose slug is not in use yet, with
    /// a suggested SKU per session. Returns how many were added. Never touches an existing row, so a staff edit
    /// survives a rerun.
    /// </summary>
    Task<int> SeedAsync(IReadOnlyList<ClassSeed> seeds, string actor, CancellationToken cancellationToken);
}
