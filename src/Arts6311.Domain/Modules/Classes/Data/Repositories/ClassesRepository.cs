using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Classes.Data.Repositories;

internal sealed class ClassesRepository(ArtsDbContext context) : IClassesRepository
{
    private const char LikeEscape = '\\';

    private DbSet<ClassOffering> Offerings => context.Set<ClassOffering>();

    public void Add(ClassOffering offering) => Offerings.Add(offering);

    public Task<ClassOffering?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Offerings
            .Include(offering => offering.Runs).ThenInclude(run => run.Sessions)
            .Include(offering => offering.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(offering => offering.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Offerings.AsNoTracking().AnyAsync(
            offering => offering.Slug == slug && (exceptId == null || offering.Id != exceptId), cancellationToken);

    public async Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        [.. await Offerings.AsNoTracking().Select(offering => offering.Slug).ToListAsync(cancellationToken)];

    public async Task<int> NextOfferingNumberAsync(CancellationToken cancellationToken) =>
        (await Offerings.AsNoTracking().MaxAsync(offering => (int?)offering.OfferingNumber, cancellationToken) ?? 0) + 1;

    public Task<List<ClassListRow>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var query = Offerings.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(offering => offering.Status == wanted);
        }

        if (!string.IsNullOrEmpty(search))
        {
            // LIKE rather than Contains: case-insensitive on SQL Server's collation and on SQLite alike, with the
            // search's own wildcards escaped so "50%" finds a percent sign.
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(offering => EF.Functions.Like(offering.Title, pattern, LikeEscape.ToString()));
        }

        if (after is not null)
        {
            // Translated to SQL's ">", under the column's own collation — the same order as the ORDER BY below.
#pragma warning disable CA1309
            query = query.Where(offering => string.Compare(offering.Title, after.Title) > 0
                || (offering.Title == after.Title && offering.Id.CompareTo(after.Id) > 0));
#pragma warning restore CA1309
        }

        return query
            .OrderBy(offering => offering.Title)
            .ThenBy(offering => offering.Id)
            .Take(take)
            .Select(offering => new ClassListRow(
                offering.Id,
                offering.Title,
                offering.Slug,
                offering.Status,
                offering.ClassType,
                offering.Level,
                offering.Runs
                    .Where(run => run.InstructorId != null)
                    .Select(run => run.InstructorId!.Value)
                    .ToList(),
                offering.HeroMediaId,
                offering.Runs.Count,
                offering.Runs
                    .SelectMany(run => run.Sessions)
                    .Where(session => session.StartsAt >= asOf)
                    .Min(session => (DateTimeOffset?)session.StartsAt),
                offering.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        await Offerings.AsNoTracking()
            .Where(offering => offering.Status == PublishStatus.Scheduled
                && offering.ScheduledFor != null && offering.ScheduledFor <= asOf)
            .OrderBy(offering => offering.ScheduledFor)
            .ThenBy(offering => offering.Id)
            .Take(take)
            .Select(offering => offering.Id)
            .ToListAsync(cancellationToken);

    public Task<List<ClassOffering>> ListPublishedAsync(
        string? slug, DateTimeOffset? endingAfter, CancellationToken cancellationToken)
    {
        var query = Offerings.AsNoTracking()
            .Include(offering => offering.Runs).ThenInclude(run => run.Sessions)
            .AsSplitQuery()
            .Where(offering => offering.Status == PublishStatus.Published);

        if (slug is not null)
        {
            query = query.Where(offering => offering.Slug == slug);
        }

        if (endingAfter is { } after)
        {
            query = query.Where(offering => offering.Runs.Any(run => run.Sessions.Any(session => session.EndsAt > after)));
        }

        return query.OrderBy(offering => offering.Id).ToListAsync(cancellationToken);
    }

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
