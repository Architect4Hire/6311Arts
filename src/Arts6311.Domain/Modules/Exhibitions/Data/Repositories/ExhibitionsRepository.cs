using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Exhibitions.Data.Repositories;

internal sealed class ExhibitionsRepository(ArtsDbContext context) : IExhibitionsRepository
{
    private const char LikeEscape = '\\';

    private DbSet<Exhibition> Exhibitions => context.Set<Exhibition>();

    public void Add(Exhibition exhibition) => Exhibitions.Add(exhibition);

    public Task<Exhibition?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Exhibitions.Include(exhibition => exhibition.Artists).FirstOrDefaultAsync(exhibition => exhibition.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Exhibitions.AsNoTracking().AnyAsync(
            exhibition => exhibition.Slug == slug && (exceptId == null || exhibition.Id != exceptId), cancellationToken);

    public async Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        [.. await Exhibitions.AsNoTracking().Select(exhibition => exhibition.Slug).ToListAsync(cancellationToken)];

    public Task<List<ExhibitionSummaryServiceModel>> ListAsync(
        ExhibitionListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken)
    {
        var query = Exhibitions.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(exhibition => exhibition.Status == wanted);
        }

        if (!string.IsNullOrEmpty(search))
        {
            // LIKE rather than Contains: case-insensitive on SQL Server's collation and on SQLite alike, with the
            // search's own wildcards escaped so "50%" finds a percent sign.
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(exhibition => EF.Functions.Like(exhibition.Title, pattern, LikeEscape.ToString()));
        }

        if (after is not null)
        {
            // Newest first: the next page is strictly earlier in (StartDate, Id), matching the ORDER BY below.
            query = query.Where(exhibition => exhibition.StartDate < after.StartDate
                || (exhibition.StartDate == after.StartDate && exhibition.Id.CompareTo(after.Id) < 0));
        }

        return query
            .OrderByDescending(exhibition => exhibition.StartDate)
            .ThenByDescending(exhibition => exhibition.Id)
            .Take(take)
            .Select(exhibition => new ExhibitionSummaryServiceModel(
                exhibition.Id,
                exhibition.Title,
                exhibition.Slug,
                exhibition.StartDate,
                exhibition.EndDate,
                exhibition.Status,
                exhibition.HeroMediaId,
                exhibition.Artists.Count,
                exhibition.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        await Exhibitions.AsNoTracking()
            .Where(exhibition => exhibition.Status == PublishStatus.Scheduled
                && exhibition.ScheduledFor != null && exhibition.ScheduledFor <= asOf)
            .OrderBy(exhibition => exhibition.ScheduledFor)
            .ThenBy(exhibition => exhibition.Id)
            .Take(take)
            .Select(exhibition => exhibition.Id)
            .ToListAsync(cancellationToken);

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
