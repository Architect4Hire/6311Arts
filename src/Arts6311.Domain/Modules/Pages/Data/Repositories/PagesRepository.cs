using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Pages.Data.Repositories;

internal sealed class PagesRepository(ArtsDbContext context) : IPagesRepository
{
    private const char LikeEscape = '\\';

    private DbSet<Page> Pages => context.Set<Page>();

    public void Add(Page page) => Pages.Add(page);

    public Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Pages.Include(page => page.Images).FirstOrDefaultAsync(page => page.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Pages.AsNoTracking().AnyAsync(page => page.Slug == slug && (exceptId == null || page.Id != exceptId), cancellationToken);

    public async Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        [.. await Pages.AsNoTracking().Select(page => page.Slug).ToListAsync(cancellationToken)];

    public Task<List<PageSummaryServiceModel>> ListAsync(
        PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken)
    {
        var query = Pages.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(page => page.Status == wanted);
        }

        if (!string.IsNullOrEmpty(search))
        {
            // LIKE rather than Contains: case-insensitive on SQL Server's collation and on SQLite alike, with the
            // search's own wildcards escaped so "50%" finds a percent sign.
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(page => EF.Functions.Like(page.Title, pattern, LikeEscape.ToString()));
        }

        if (after is not null)
        {
            // Translated to SQL's ">", under the column's own collation — the same order as the ORDER BY below.
#pragma warning disable CA1309
            query = query.Where(page => string.Compare(page.Title, after.Title) > 0
                || (page.Title == after.Title && page.Id.CompareTo(after.Id) > 0));
#pragma warning restore CA1309
        }

        return query
            .OrderBy(page => page.Title)
            .ThenBy(page => page.Id)
            .Take(take)
            .Select(page => new PageSummaryServiceModel(page.Id, page.Title, page.Slug, page.Status, page.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
