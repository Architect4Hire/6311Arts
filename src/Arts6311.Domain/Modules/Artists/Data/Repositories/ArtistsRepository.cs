using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Artists.Data.Repositories;

internal sealed class ArtistsRepository(ArtsDbContext context) : IArtistsRepository
{
    private const char LikeEscape = '\\';

    private DbSet<Artist> Artists => context.Set<Artist>();

    public void Add(Artist artist) => Artists.Add(artist);

    public Task<Artist?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Artists.FirstOrDefaultAsync(artist => artist.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Artists.AsNoTracking().AnyAsync(
            artist => artist.Slug == slug && (exceptId == null || artist.Id != exceptId), cancellationToken);

    public async Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        [.. await Artists.AsNoTracking().Select(artist => artist.Slug).ToListAsync(cancellationToken)];

    public Task<List<ArtistSummaryServiceModel>> ListAsync(
        ArtistListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken)
    {
        var query = Artists.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(artist => artist.Status == wanted);
        }

        if (!string.IsNullOrEmpty(search))
        {
            // LIKE rather than Contains: case-insensitive on SQL Server's collation and on SQLite alike, with the
            // search's own wildcards escaped so "50%" finds a percent sign.
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(artist => EF.Functions.Like(artist.Name, pattern, LikeEscape.ToString()));
        }

        if (after is not null)
        {
            // Translated to SQL's ">", under the column's own collation — the same order as the ORDER BY below, which
            // is what a keyset needs. An ordinal comparison would disagree with that order, and EF cannot translate one.
#pragma warning disable CA1309
            query = query.Where(artist => string.Compare(artist.Name, after.Name) > 0
                || (artist.Name == after.Name && artist.Id.CompareTo(after.Id) > 0));
#pragma warning restore CA1309
        }

        return query
            .OrderBy(artist => artist.Name)
            .ThenBy(artist => artist.Id)
            .Take(take)
            .Select(artist => new ArtistSummaryServiceModel(
                artist.Id,
                artist.Name,
                artist.Slug,
                artist.Medium,
                artist.Active,
                artist.Status,
                artist.PortraitMediaId,
                artist.UpdatedAt))
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
