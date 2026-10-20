using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Redirects.Facade;

/// <summary>
/// The Redirects module's boundary: the CMS routes (Admin only, audited), the site's list, and the slug-change
/// method the Worker calls for <c>redirects.slug-changed</c> on behalf of every content module (ADR 0035).
/// </summary>
public interface IRedirectsFacade
{
    Task<OperationResult<CursorPage<RedirectServiceModel>>> ListAsync(
        RedirectListViewModel request, CancellationToken cancellationToken);

    /// <summary>Every redirect as the site applies them. Cached for <c>RedirectPolicy.SiteCacheLifetime</c>.</summary>
    Task<OperationResult<IReadOnlyList<SiteRedirectServiceModel>>> ListForSiteAsync(CancellationToken cancellationToken);

    Task<OperationResult<RedirectServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// A staff redirect. Refusals: <c>validation.failed</c>, <c>redirects.from_taken</c>,
    /// <c>redirects.loop_or_chain</c>.
    /// </summary>
    Task<OperationResult<RedirectServiceModel>> CreateAsync(
        RedirectCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <summary>An edit. Refusals as for create, plus <c>resource.not_found</c> and <c>concurrency.*</c>.</summary>
    Task<OperationResult<RedirectServiceModel>> UpdateAsync(
        Guid id, RedirectUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A published record's address moved: its old address now leads to the new one, every chain stays one hop,
    /// and an address that is live again stops redirecting. Idempotent and safe out of order (ADR 0035).
    /// </summary>
    Task<OperationResult> ApplySlugChangeAsync(RedirectSlugChangeViewModel change, CancellationToken cancellationToken);
}
