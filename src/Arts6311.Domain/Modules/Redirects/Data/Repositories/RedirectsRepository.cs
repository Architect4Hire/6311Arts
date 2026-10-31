using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Redirects.Data.Repositories;

internal sealed class RedirectsRepository(ArtsDbContext context) : IRedirectsRepository
{
    private const char LikeEscape = '\\';

    private DbSet<Redirect> Redirects => context.Set<Redirect>();

    public void Add(Redirect redirect) => Redirects.Add(redirect);

    public void Remove(Redirect redirect) => Redirects.Remove(redirect);

    public Task<Redirect?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Redirects.FirstOrDefaultAsync(redirect => redirect.Id == id, cancellationToken);

    public Task<Redirect?> FindByFromAsync(string fromPath, Guid? exceptId, CancellationToken cancellationToken) =>
        Redirects.AsNoTracking().FirstOrDefaultAsync(
            redirect => redirect.FromPath == fromPath && (exceptId == null || redirect.Id != exceptId), cancellationToken);

    public Task<Redirect?> FindTargetingAsync(string targetPath, Guid? exceptId, CancellationToken cancellationToken) =>
        Redirects.AsNoTracking().FirstOrDefaultAsync(
            redirect => redirect.TargetPath == targetPath && (exceptId == null || redirect.Id != exceptId), cancellationToken);

    public Task<List<Redirect>> ListForSlugChangeAsync(string fromPath, string toPath, CancellationToken cancellationToken) =>
        Redirects
            .Where(redirect => redirect.FromPath == fromPath || redirect.FromPath == toPath || redirect.TargetPath == fromPath)
            .ToListAsync(cancellationToken);

    public Task<List<RedirectServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, RedirectSource? source, string? search, CancellationToken cancellationToken)
    {
        var query = Redirects.AsNoTracking();

        if (source is { } wanted)
        {
            query = query.Where(redirect => redirect.Source == wanted);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(redirect =>
                EF.Functions.Like(redirect.FromPath, pattern, LikeEscape.ToString())
                || EF.Functions.Like(redirect.ToPath, pattern, LikeEscape.ToString()));
        }

        return RedirectListOrder.Columns.Apply(query, sort, after)
            .Take(take)
            .Select(redirect => new RedirectServiceModel(
                redirect.Id,
                redirect.FromPath,
                redirect.ToPath,
                redirect.StatusCode,
                redirect.Source,
                redirect.LegacyWordPressId,
                redirect.CreatedAt,
                redirect.UpdatedAt,
                redirect.UpdatedBy,
                EntityTag.Format(redirect.RowVersion)))
            .ToListAsync(cancellationToken);
    }

    public Task<List<SiteRedirectServiceModel>> ListForSiteAsync(CancellationToken cancellationToken) =>
        Redirects.AsNoTracking()
            .OrderBy(redirect => redirect.FromPath)
            .Select(redirect => new SiteRedirectServiceModel(redirect.FromPath, redirect.ToPath, redirect.StatusCode))
            .ToListAsync(cancellationToken);

    public Task<bool> HasBeenSeededAsync(CancellationToken cancellationToken) =>
        context.AuditLogs.AsNoTracking().AnyAsync(
            entry => entry.Action == RedirectAudit.Seeded && entry.ResourceType == RedirectAudit.ResourceType, cancellationToken);

    public async Task<HashSet<string>> GetFromPathsAsync(CancellationToken cancellationToken) =>
        [.. await Redirects.AsNoTracking().Select(redirect => redirect.FromPath).ToListAsync(cancellationToken)];

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
