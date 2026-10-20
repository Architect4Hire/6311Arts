using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Artists.Business;

internal interface IArtistsBusiness
{
    Task<OperationResult<CursorPage<ArtistSummaryServiceModel>>> ListAsync(
        ArtistListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ArtistServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The artists among <paramref name="ids"/> that exist. Empty in, empty out, without a query.</summary>
    Task<IReadOnlyList<ArtistSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The artists among <paramref name="slugs"/> that exist. Empty in, empty out, without a query.</summary>
    Task<IReadOnlyList<ArtistSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    Task<OperationResult<ArtistServiceModel>> CreateAsync(
        ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <param name="portrait">What Media said about <c>update.PortraitMediaId</c>, when one is sent.</param>
    Task<OperationResult<ArtistServiceModel>> UpdateAsync(
        Guid id,
        ArtistUpdateViewModel update,
        string? ifMatch,
        ArtistPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The development roster: adds, as Published and without portraits, each artist whose slug is not in use yet.
    /// Returns how many were added. Never touches an existing row, so a staff edit survives a rerun.
    /// </summary>
    Task<int> SeedAsync(IReadOnlyList<(string Name, string? Medium)> roster, string actor, CancellationToken cancellationToken);

    /// <param name="portrait">Required for <see cref="PublishCommand.Publish"/>: what Media said about the portrait.</param>
    Task<OperationResult<ArtistServiceModel>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ArtistPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled for <paramref name="scheduledFor"/>. Refusals: <c>concurrency.*</c>,
    /// <c>cms.status_transition_invalid</c>, and <c>validation.failed</c> for a time already past or a portrait
    /// that could not be published (ADR 0036). The <c>schedule</c> command is its own method rather than another
    /// <see cref="TransitionAsync"/> argument, because it is the one command with a body.
    /// </summary>
    /// <param name="portrait">What Media said about the artist's current portrait, as for a publish.</param>
    Task<OperationResult<ArtistServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        ArtistPortraitCheck? portrait,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<ArtistServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The artists due to be published at <paramref name="asOf"/>, for the Worker's scheduled publisher.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one due artist on the scheduled publisher's behalf, re-reading it first: the row version is the
    /// claim, so two Workers acting on one artist produce one publish and one <see cref="ScheduledPublishOutcome.Lost"/>.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, ArtistPortraitCheck? portrait, CancellationToken cancellationToken);
}
