using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Redirects.Data.Repositories;

internal interface IRedirectsRepository
{
    void Add(Redirect redirect);

    void Remove(Redirect redirect);

    /// <summary>One redirect, tracked, for an edit or a delete.</summary>
    Task<Redirect?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The redirect leaving from <paramref name="fromPath"/>, other than <paramref name="exceptId"/>, untracked.</summary>
    Task<Redirect?> FindByFromAsync(string fromPath, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>A redirect arriving at <paramref name="targetPath"/>, other than <paramref name="exceptId"/>, untracked.</summary>
    Task<Redirect?> FindTargetingAsync(string targetPath, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>
    /// Everything a slug change from <paramref name="fromPath"/> to <paramref name="toPath"/> touches, tracked: the
    /// redirects leaving either address, and every redirect arriving at the old one.
    /// </summary>
    Task<List<Redirect>> ListForSlugChangeAsync(string fromPath, string toPath, CancellationToken cancellationToken);

    /// <summary>One page of the CMS list in FromPath order, projected straight to rows.</summary>
    Task<List<RedirectServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, RedirectSource? source, string? search, CancellationToken cancellationToken);

    /// <summary>Every redirect, as the site applies them, in FromPath order.</summary>
    Task<List<SiteRedirectServiceModel>> ListForSiteAsync(CancellationToken cancellationToken);

    /// <summary>Whether the legacy seed has ever run here (its audit entry is the marker).</summary>
    Task<bool> HasBeenSeededAsync(CancellationToken cancellationToken);

    /// <summary>Every old address in use. For the seed, which adds only what is missing.</summary>
    Task<HashSet<string>> GetFromPathsAsync(CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
