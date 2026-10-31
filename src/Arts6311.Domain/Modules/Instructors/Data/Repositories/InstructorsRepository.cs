using Arts6311.Domain.Managers.Paging;
using System.Linq.Expressions;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Instructors.Data.Repositories;

internal sealed class InstructorsRepository(ArtsDbContext context) : IInstructorsRepository
{
    private const char LikeEscape = '\\';

    /// <summary>One list row, translated by EF into the query's column list.</summary>
    private static readonly Expression<Func<Instructor, InstructorSummaryServiceModel>> ToSummary = instructor => new InstructorSummaryServiceModel(
        instructor.Id,
        instructor.Name,
        instructor.Slug,
        instructor.Medium,
        instructor.Active,
        instructor.Status,
        instructor.PortraitMediaId,
        instructor.UpdatedAt);

    private DbSet<Instructor> Instructors => context.Set<Instructor>();

    public void Add(Instructor instructor) => Instructors.Add(instructor);

    public Task<Instructor?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Instructors.FirstOrDefaultAsync(instructor => instructor.Id == id, cancellationToken);

    public Task<Instructor?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        Instructors.IgnoreQueryFilters()
            .FirstOrDefaultAsync(instructor => instructor.Id == id && instructor.DeletedAt != null, cancellationToken);

    public Task<List<InstructorDeletedRow>> ListDeletedAsync(
        InstructorListPosition? after, int take, string? search, CancellationToken cancellationToken)
    {
        // The one list that asks past the query filter, and then for the deleted rows alone (ADR 0052).
        var query = Instructors.IgnoreQueryFilters().AsNoTracking().Where(instructor => instructor.DeletedAt != null);

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(instructor => EF.Functions.Like(instructor.Name, pattern, LikeEscape.ToString()));
        }

        if (after is not null)
        {
            // The same order as the main list, so one cursor shape serves both.
            #pragma warning disable CA1309
            query = query.Where(instructor => string.Compare(instructor.Name, after.Name) > 0
                || (instructor.Name == after.Name && instructor.Id.CompareTo(after.Id) > 0));
#pragma warning restore CA1309
        }

        return query
            .OrderBy(instructor => instructor.Name)
            .ThenBy(instructor => instructor.Id)
            .Take(take)
            .Select(instructor => new InstructorDeletedRow(
                instructor.Id, instructor.Name, instructor.Slug, instructor.Status, instructor.DeletedAt!.Value, instructor.RowVersion))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Instructors.AsNoTracking().AnyAsync(
            instructor => instructor.Slug == slug && (exceptId == null || instructor.Id != exceptId), cancellationToken);

    /// <remarks>Deleted records included, so a seed or a re-import never recreates one (ADR 0052).</remarks>
    public async Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        [.. await Instructors.IgnoreQueryFilters().AsNoTracking().Select(instructor => instructor.Slug).ToListAsync(cancellationToken)];

    public Task<List<InstructorSummaryServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken)
    {
        var query = Instructors.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(instructor => instructor.Status == wanted);
        }

        if (!string.IsNullOrEmpty(search))
        {
            // LIKE rather than Contains: case-insensitive on SQL Server's collation and on SQLite alike, with the
            // search's own wildcards escaped so "50%" finds a percent sign.
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(instructor => EF.Functions.Like(instructor.Name, pattern, LikeEscape.ToString()));
        }

        return InstructorListOrder.Columns.Apply(query, sort, after)
            .Take(take)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    public Task<List<InstructorSummaryServiceModel>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Instructors.AsNoTracking().Where(instructor => ids.Contains(instructor.Id)).Select(ToSummary).ToListAsync(cancellationToken);

    public Task<List<InstructorSummaryServiceModel>> ListBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        Instructors.AsNoTracking().Where(instructor => slugs.Contains(instructor.Slug)).Select(ToSummary).ToListAsync(cancellationToken);

    public Task<List<InstructorSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken)
    {
        var query = Instructors.AsNoTracking().Where(instructor => instructor.Status == PublishStatus.Published);
        if (slug is not null)
        {
            query = query.Where(instructor => instructor.Slug == slug);
        }

        return query
            .OrderBy(instructor => instructor.Name)
            .ThenBy(instructor => instructor.Id)
            .Select(instructor => new InstructorSiteRow(
                instructor.Id, instructor.Slug, instructor.Name, instructor.Medium, instructor.Active,
                instructor.PortraitMediaId, instructor.Bio, instructor.Website, instructor.Instagram, instructor.ArtistId,
                instructor.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        await Instructors.AsNoTracking()
            .Where(instructor => instructor.Status == PublishStatus.Scheduled && instructor.ScheduledFor != null && instructor.ScheduledFor <= asOf)
            .OrderBy(instructor => instructor.ScheduledFor)
            .ThenBy(instructor => instructor.Id)
            .Take(take)
            .Select(instructor => instructor.Id)
            .ToListAsync(cancellationToken);

    public Task<List<string>> ListNamingArtistAsync(Guid artistId, int take, CancellationToken cancellationToken) =>
        Instructors.AsNoTracking()
            .Where(instructor => instructor.ArtistId == artistId)
            .OrderBy(instructor => instructor.Name)
            .Take(take)
            .Select(instructor => instructor.Name)
            .ToListAsync(cancellationToken);

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
