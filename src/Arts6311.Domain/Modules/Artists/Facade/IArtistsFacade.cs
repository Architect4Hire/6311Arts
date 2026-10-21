using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Artists.Facade;

/// <summary>
/// The Artists module's boundary: the CMS artist routes today; the site's read routes (3.1), Exhibitions' artist
/// picker (2.5) and the importer (4.x) later. Every write takes the row version as <c>ifMatch</c>.
/// </summary>
public interface IArtistsFacade
{
    Task<OperationResult<CursorPage<ArtistSummaryServiceModel>>> ListAsync(
        ArtistListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ArtistServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// For another module (Exhibitions' credits): the artists among <paramref name="ids"/> that exist, whatever their
    /// status, in no particular order. An id with no artist is simply absent. At most
    /// <see cref="Managers.ArtistPolicy.LookupMaxCount"/> ids.
    /// </summary>
    Task<IReadOnlyList<ArtistSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="GetSummariesAsync"/>, by slug: for the development seed today, and the importer and the site's
    /// artist pages later. At most <see cref="Managers.ArtistPolicy.LookupMaxCount"/> slugs.
    /// </summary>
    Task<IReadOnlyList<ArtistSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>
    /// Every published artist, by name, as the site shows them. Not cached here: the one caller, the Site module,
    /// caches what it composes from this (ADR 0042).
    /// </summary>
    Task<IReadOnlyList<SiteArtistServiceModel>> ListForSiteAsync(CancellationToken cancellationToken);

    /// <summary>The published artist at <paramref name="slug"/>, or <c>null</c>. Not cached here, as above.</summary>
    Task<SiteArtistServiceModel?> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>One artist as the site would show them, whatever the status, for a preview link. Never cached.</summary>
    Task<OperationResult<PreviewRecord<SiteArtistServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// For another module's site reads (exhibitions, artwork): how each of these artists is credited. An id with no
    /// artist is simply absent. Any number of ids: a whole list's credits are asked
    /// <see cref="Managers.ArtistPolicy.LookupMaxCount"/> at a time.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, SiteArtistCreditServiceModel>> DescribeCreditsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Every published artist's address, for <c>/sitemap.xml</c>.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);

    /// <summary>A new Draft. Refusals: <c>validation.failed</c> (including a portrait not in the library), <c>cms.slug_taken</c>.</summary>
    Task<OperationResult<ArtistServiceModel>> CreateAsync(
        ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A staff edit. Refusals: <c>validation.failed</c>, <c>resource.not_found</c>, <c>concurrency.*</c>,
    /// <c>cms.slug_taken</c>. A published slug may change: the old address is redirected (ADR 0035).
    /// </summary>
    Task<OperationResult<ArtistServiceModel>> UpdateAsync(
        Guid id, ArtistUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft → Published. Refused while the portrait lacks alt text (<c>validation.failed</c> on <c>portraitMediaId</c>).</summary>
    Task<OperationResult<ArtistServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Published → Draft. The slug stays locked.</summary>
    Task<OperationResult<ArtistServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft or Published → Archived. Admin only at the route (auth.md "Sensitive operations"); audited.</summary>
    Task<OperationResult<ArtistServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled, for the Worker to publish when the time comes (ADR 0036). Refusals:
    /// <c>validation.failed</c> (a time already past, or a portrait that could not be published),
    /// <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.status_transition_invalid</c>.
    /// </summary>
    Task<OperationResult<ArtistServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<ArtistServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher (<see cref="IScheduledPublisher"/>): the artists due at
    /// <paramref name="asOf"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher: publishes one due artist, re-read first, claimed by its row version.
    /// Never throws for an artist that moved on — it reports what happened instead.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);
}
