using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Redirects.Business;

internal interface IRedirectsBusiness
{
    Task<OperationResult<CursorPage<RedirectServiceModel>>> ListAsync(
        RedirectListViewModel request, CancellationToken cancellationToken);

    Task<IReadOnlyList<SiteRedirectServiceModel>> ListForSiteAsync(CancellationToken cancellationToken);

    Task<OperationResult<RedirectServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<RedirectServiceModel>> CreateAsync(
        RedirectCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<RedirectServiceModel>> UpdateAsync(
        Guid id, RedirectUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A published record's address moved: its old address leads to the new one, one hop, whatever order renames
    /// arrive in (ADR 0035). Idempotent: applying the same change twice changes nothing the second time.
    /// </summary>
    Task<OperationResult> ApplySlugChangeAsync(RedirectSlugChangeViewModel change, CancellationToken cancellationToken);

    /// <summary>Adds each of <paramref name="seeds"/> whose old address is free. Returns how many were added.</summary>
    Task<int> SeedAsync(
        IReadOnlyList<(string FromPath, string ToPath)> seeds, string actor, CancellationToken cancellationToken);
}
