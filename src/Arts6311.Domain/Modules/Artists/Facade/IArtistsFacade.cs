using Arts6311.Domain.Managers.Paging;
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
}
