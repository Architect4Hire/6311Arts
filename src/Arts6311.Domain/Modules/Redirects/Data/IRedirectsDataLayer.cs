using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Redirects.Data;

internal enum RedirectWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another redirect took the old address between Business's check and the save.</summary>
    FromTaken = 20,
}

/// <summary>What one slug change does to the table, as Business decided it: rows to retire, rewrite and add.</summary>
/// <param name="Remove">Tracked rows to delete.</param>
/// <param name="Changed">Tracked rows Business already rewrote.</param>
/// <param name="Add">New rows.</param>
internal sealed record RedirectSlugChangePlan(
    IReadOnlyList<Redirect> Remove, IReadOnlyList<Redirect> Changed, IReadOnlyList<Redirect> Add)
{
    public bool IsEmpty => Remove.Count == 0 && Changed.Count == 0 && Add.Count == 0;
}

internal interface IRedirectsDataLayer
{
    Task<Redirect?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Redirect?> FindByFromAsync(string fromPath, Guid? exceptId, CancellationToken cancellationToken);

    Task<Redirect?> FindTargetingAsync(string targetPath, Guid? exceptId, CancellationToken cancellationToken);

    Task<List<Redirect>> ListForSlugChangeAsync(string fromPath, string toPath, CancellationToken cancellationToken);

    Task<List<RedirectServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, RedirectSource? source, string? search, CancellationToken cancellationToken);

    Task<List<SiteRedirectServiceModel>> ListForSiteAsync(CancellationToken cancellationToken);

    /// <summary>Adds the redirect Business built, with its bookkeeping and audit entry, in one save.</summary>
    Task<RedirectWriteOutcome> CreateAsync(Redirect redirect, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Saves an edit already applied to <paramref name="redirect"/>, with its audit entry, in one save.</summary>
    Task<RedirectWriteOutcome> UpdateAsync(
        Redirect redirect, string actorUserId, string before, CancellationToken cancellationToken);

    /// <summary>Deletes the redirect, with its audit entry, in one save.</summary>
    Task<RedirectWriteOutcome> DeleteAsync(Redirect redirect, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Applies a slug change's plan, with one audit entry naming the rename, in one save.</summary>
    Task<RedirectWriteOutcome> ApplySlugChangeAsync(
        RedirectSlugChangePlan plan, string summary, string resourceId, CancellationToken cancellationToken);

    Task<HashSet<string>> GetFromPathsAsync(CancellationToken cancellationToken);

    Task<bool> HasBeenSeededAsync(CancellationToken cancellationToken);

    /// <summary>Adds the seed redirects Business chose, with one audit entry, in one save. Returns how many.</summary>
    Task<int> AddSeedAsync(IReadOnlyList<Redirect> redirects, string actor, CancellationToken cancellationToken);
}
