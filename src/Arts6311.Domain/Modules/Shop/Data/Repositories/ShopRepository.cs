using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Shop.Data.Repositories;

internal sealed class ShopRepository(ArtsDbContext context) : IShopRepository
{
    private const char LikeEscape = '\\';

    private DbSet<ShopItem> Items => context.Set<ShopItem>();

    public void Add(ShopItem item) => Items.Add(item);

    public Task<ShopItem?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Items
            .Include(item => item.Categories)
            .Include(item => item.Images)
            .Include(item => item.DescriptionImages)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Items.AsNoTracking().AnyAsync(
            item => item.Slug == slug && (exceptId == null || item.Id != exceptId), cancellationToken);

    public async Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        [.. await Items.AsNoTracking().Select(item => item.Slug).ToListAsync(cancellationToken)];

    public async Task<int> NextItemNumberAsync(CancellationToken cancellationToken) =>
        (await Items.AsNoTracking().MaxAsync(item => (int?)item.ItemNumber, cancellationToken) ?? 0) + 1;

    public Task<List<ShopItemListRow>> ListAsync(
        ShopItemListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        string? category,
        CancellationToken cancellationToken)
    {
        var query = Items.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(item => item.Status == wanted);
        }

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(item => item.Categories.Any(row => row.Category == category));
        }

        if (!string.IsNullOrEmpty(search))
        {
            // LIKE rather than Contains: case-insensitive on SQL Server's collation and on SQLite alike, with the
            // search's own wildcards escaped so "50%" finds a percent sign.
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(item => EF.Functions.Like(item.Title, pattern, LikeEscape.ToString()));
        }

        if (after is not null)
        {
            // Translated to SQL's ">", under the column's own collation — the same order as the ORDER BY below.
#pragma warning disable CA1309
            query = query.Where(item => string.Compare(item.Title, after.Title) > 0
                || (item.Title == after.Title && item.Id.CompareTo(after.Id) > 0));
#pragma warning restore CA1309
        }

        return query
            .OrderBy(item => item.Title)
            .ThenBy(item => item.Id)
            .Take(take)
            .Select(item => new ShopItemListRow(
                item.Id,
                item.Title,
                item.Slug,
                item.Status,

                // Which artist to ask Artists about; the facade describes them (ADR 0033).
                item.ArtistId,
                item.Categories.Select(row => row.Category).ToList(),
                item.Images.OrderBy(image => image.Position).Select(image => (Guid?)image.MediaAssetId).FirstOrDefault(),
                item.Sku,
                item.OneOfAKind,
                item.PriceCents,
                item.StockOnHand,
                item.SoldOut,
                item.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        await Items.AsNoTracking()
            .Where(item => item.Status == PublishStatus.Scheduled
                && item.ScheduledFor != null && item.ScheduledFor <= asOf)
            .OrderBy(item => item.ScheduledFor)
            .ThenBy(item => item.Id)
            .Take(take)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
