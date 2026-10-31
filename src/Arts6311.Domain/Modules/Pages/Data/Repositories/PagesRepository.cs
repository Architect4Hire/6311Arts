using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers;
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

    public Task<Page?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        Pages.IgnoreQueryFilters()
            .Include(page => page.Images)
            .FirstOrDefaultAsync(page => page.Id == id && page.DeletedAt != null, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Pages.AsNoTracking().AnyAsync(page => page.Slug == slug && (exceptId == null || page.Id != exceptId), cancellationToken);

    /// <remarks>Deleted pages included, so the placeholder seed never recreates one (<see cref="IPagesRepository"/>).</remarks>
    public async Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        [.. await Pages.IgnoreQueryFilters().AsNoTracking().Select(page => page.Slug).ToListAsync(cancellationToken)];

    public Task<List<PageSummaryServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken)
    {
        var query = Pages.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(page => page.Status == wanted);
        }

        query = WithSearch(query, search);
        return PageListOrder.Columns.Apply(query, sort, after)
            .Take(take)
            .Select(page => new PageSummaryServiceModel(page.Id, page.Title, page.Slug, page.Status, page.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public Task<List<PageDeletedRow>> ListDeletedAsync(
        PageListPosition? after, int take, string? search, CancellationToken cancellationToken)
    {
        // The one list that asks past the query filter, and then for the deleted rows alone (ADR 0052).
        var query = Pages.IgnoreQueryFilters().AsNoTracking().Where(page => page.DeletedAt != null);

        query = WithSearch(query, search);
        query = WithPosition(query, after);

        return query
            .OrderBy(page => page.Title)
            .ThenBy(page => page.Id)
            .Take(take)
            .Select(page => new PageDeletedRow(
                page.Id, page.Title, page.Slug, page.Status, page.DeletedAt!.Value, page.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        await Pages.AsNoTracking()
            .Where(page => page.Status == PublishStatus.Scheduled && page.ScheduledFor != null && page.ScheduledFor <= asOf)
            .OrderBy(page => page.ScheduledFor)
            .ThenBy(page => page.Id)
            .Take(take)
            .Select(page => page.Id)
            .ToListAsync(cancellationToken);

    public Task<SitePageServiceModel?> FindPublishedAsync(string slug, CancellationToken cancellationToken) =>
        Pages.AsNoTracking()
            .Where(page => page.Slug == slug && page.Status == PublishStatus.Published)
            .Select(page => new SitePageServiceModel(page.Id, page.Slug, page.Title, page.Body, page.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<List<(string Slug, DateTimeOffset UpdatedAt)>> ListPublishedAddressesAsync(CancellationToken cancellationToken)
    {
        var rows = await Pages.AsNoTracking()
            .Where(page => page.Status == PublishStatus.Published)
            .OrderBy(page => page.Slug)
            .Select(page => new { page.Slug, page.UpdatedAt })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => (row.Slug, row.UpdatedAt))];
    }

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    /// <summary>Part of a title, matched without regard to case. Shared by the list and the Deleted view.</summary>
    private static IQueryable<Page> WithSearch(IQueryable<Page> query, string? search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return query;
        }

        // LIKE rather than Contains: case-insensitive on SQL Server's collation and on SQLite alike, with the
        // search's own wildcards escaped so "50%" finds a percent sign.
        var pattern = $"%{EscapeLike(search)}%";
        return query.Where(page => EF.Functions.Like(page.Title, pattern, LikeEscape.ToString()));
    }

    /// <summary>Where a keyset page resumes: after this row in the (Title, Id) order both lists are read in.</summary>
    private static IQueryable<Page> WithPosition(IQueryable<Page> query, PageListPosition? after)
    {
        if (after is null)
        {
            return query;
        }

        // Translated to SQL's ">", under the column's own collation — the same order as the ORDER BY.
#pragma warning disable CA1309
        return query.Where(page => string.Compare(page.Title, after.Title) > 0
            || (page.Title == after.Title && page.Id.CompareTo(after.Id) > 0));
#pragma warning restore CA1309
    }

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
