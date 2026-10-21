using System.Linq.Expressions;
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

    public Task<List<ShopItemSiteRow>> ListPublishedAsync(ShopSiteQuery query, CancellationToken cancellationToken)
    {
        var items = PublishedMatching(query.Category, query.ArtistId);

        // Each order is a keyset with the id as the tie-breaker, and the WHERE resumes in exactly the ORDER BY's
        // terms, so a page boundary neither skips nor repeats a row. "Unpriced last" is the leading ORDER BY term
        // in both price orders, so the keyset treats null as a value past every price (ADR 0042).
        var after = query.After;
        IOrderedQueryable<ShopItem> ordered;
        switch (query.Sort)
        {
            case ShopSiteSort.PriceAscending:
                if (after is not null)
                {
                    items = after.PriceCents is { } price
                        ? items.Where(item => item.PriceCents == null || item.PriceCents > price
                            || (item.PriceCents == price && item.Id.CompareTo(after.Id) > 0))
                        : items.Where(item => item.PriceCents == null && item.Id.CompareTo(after.Id) > 0);
                }

                ordered = items.OrderBy(item => item.PriceCents == null).ThenBy(item => item.PriceCents).ThenBy(item => item.Id);
                break;

            case ShopSiteSort.PriceDescending:
                if (after is not null)
                {
                    items = after.PriceCents is { } price
                        ? items.Where(item => item.PriceCents == null || item.PriceCents < price
                            || (item.PriceCents == price && item.Id.CompareTo(after.Id) > 0))
                        : items.Where(item => item.PriceCents == null && item.Id.CompareTo(after.Id) > 0);
                }

                ordered = items.OrderBy(item => item.PriceCents == null).ThenByDescending(item => item.PriceCents).ThenBy(item => item.Id);
                break;

            case ShopSiteSort.Title:
                if (after is not null)
                {
                    var title = after.Title ?? string.Empty;

                    // Translated to SQL's ">", under the column's own collation — the same order as the ORDER BY below.
#pragma warning disable CA1309
                    items = items.Where(item => string.Compare(item.Title, title) > 0
                        || (item.Title == title && item.Id.CompareTo(after.Id) > 0));
#pragma warning restore CA1309
                }

                ordered = items.OrderBy(item => item.Title).ThenBy(item => item.Id);
                break;

            default:
                if (after is not null)
                {
                    var publishedAt = after.PublishedAt;
                    items = items.Where(item => item.PublishedAt < publishedAt
                        || (item.PublishedAt == publishedAt && item.Id.CompareTo(after.Id) < 0));
                }

                ordered = items.OrderByDescending(item => item.PublishedAt).ThenByDescending(item => item.Id);
                break;
        }

        return ordered.Take(query.Take).Select(SiteRow).ToListAsync(cancellationToken);
    }

    public Task<int> CountPublishedAsync(string? category, Guid? artistId, CancellationToken cancellationToken) =>
        PublishedMatching(category, artistId).CountAsync(cancellationToken);

    /// <summary>The site's filters, shared by the page and its count so the two can never disagree.</summary>
    private IQueryable<ShopItem> PublishedMatching(string? category, Guid? artistId)
    {
        var items = Items.AsNoTracking().Where(item => item.Status == PublishStatus.Published);

        if (category is not null)
        {
            items = items.Where(item => item.Categories.Any(row => row.Category == category));
        }

        if (artistId is { } artist)
        {
            items = items.Where(item => item.ArtistId == artist);
        }

        return items;
    }

    public Task<ShopItemSiteRow?> FindPublishedAsync(string slug, CancellationToken cancellationToken) =>
        Items.AsNoTracking()
            .Where(item => item.Slug == slug && item.Status == PublishStatus.Published)
            .Select(SiteRow)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<Dictionary<Guid, int>> CountPublishedByArtistAsync(CancellationToken cancellationToken)
    {
        var rows = await Items.AsNoTracking()
            .Where(item => item.Status == PublishStatus.Published && item.ArtistId != null)
            .GroupBy(item => item.ArtistId!.Value)
            .Select(group => new { ArtistId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.ArtistId, row => row.Count);
    }

    public async Task<List<(string Slug, DateTimeOffset UpdatedAt)>> ListPublishedAddressesAsync(CancellationToken cancellationToken)
    {
        var rows = await Items.AsNoTracking()
            .Where(item => item.Status == PublishStatus.Published)
            .OrderBy(item => item.Slug)
            .Select(item => new { item.Slug, item.UpdatedAt })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => (row.Slug, row.UpdatedAt))];
    }

    /// <summary>The site row's projection, in one place for the list and the single read.</summary>
    private static readonly Expression<Func<ShopItem, ShopItemSiteRow>> SiteRow = item => new ShopItemSiteRow(
        item.Id,
        item.Slug,
        item.Title,
        item.Description,

        // Which artist to ask Artists about; the facade describes them (ADR 0033).
        item.ArtistId,
        item.Categories.OrderBy(row => row.Category).Select(row => row.Category).ToList(),
        item.Images.OrderBy(image => image.Position).Select(image => image.MediaAssetId).ToList(),
        item.PriceCents,
        item.OneOfAKind,
        item.SoldOut,
        item.PublishedAt,
        item.UpdatedAt);

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
