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

    /// <summary>The Deleted view: the records staff deleted, on this list's own cursor scope (ADR 0052).</summary>
    Task<OperationResult<CursorPage<ArtistDeletedServiceModel>>> ListDeletedAsync(
        ArtistListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the artist, whatever its status: it leaves the site, leaves the CMS list and releases its web
    /// address, and only the Deleted view and the audit trail still name it (ADR 0052). Refusals:
    /// <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.in_use</c>.
    /// </summary>
    Task<OperationResult> DeleteAsync(
        Guid id, ContentReferenceCheck references, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings a deleted artist back as a Draft, with any schedule cleared: nothing returns to the site by being
    /// restored (ADR 0052). Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.slug_taken</c>.
    /// </summary>
    Task<OperationResult<ArtistServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The artists among <paramref name="ids"/> that exist. Empty in, empty out, without a query.</summary>
    Task<IReadOnlyList<ArtistSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The artists among <paramref name="slugs"/> that exist. Empty in, empty out, without a query.</summary>
    Task<IReadOnlyList<ArtistSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>The artists named one of <paramref name="names"/>, whatever the case. Empty in, empty out, without a query.</summary>
    Task<IReadOnlyList<ArtistSummaryServiceModel>> FindByNamesAsync(
        IReadOnlyCollection<string> names, CancellationToken cancellationToken);

    /// <summary>Every published artist, by name, for the site (ADR 0042).</summary>
    Task<IReadOnlyList<ArtistSiteRow>> ListForSiteAsync(CancellationToken cancellationToken);

    /// <summary>The published artist at <paramref name="slug"/>, or <c>null</c>: a draft is not there to the site.</summary>
    Task<ArtistSiteRow?> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>One artist as the site would show them, whatever the status, for a preview link.</summary>
    Task<OperationResult<PreviewRecord<ArtistSiteRow>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The credits for <paramref name="ids"/>, as another record shows them: the name always, the page only when the
    /// artist is published.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel>> DescribeCreditsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every published artist's address and last change, for the sitemap.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);

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
