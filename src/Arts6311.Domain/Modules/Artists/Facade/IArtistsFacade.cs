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

    /// <summary>A new Draft. Refusals: <c>validation.failed</c> (including a portrait not in the library), <c>cms.slug_taken</c>.</summary>
    Task<OperationResult<ArtistServiceModel>> CreateAsync(
        ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A staff edit. Refusals: <c>validation.failed</c>, <c>resource.not_found</c>, <c>concurrency.*</c>,
    /// <c>cms.slug_taken</c>, <c>cms.slug_locked</c>.
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
