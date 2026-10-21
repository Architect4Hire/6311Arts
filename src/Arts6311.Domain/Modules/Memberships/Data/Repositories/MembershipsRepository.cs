using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Memberships.Data.Repositories;

internal sealed class MembershipsRepository(ArtsDbContext context) : IMembershipsRepository
{
    private const char LikeEscape = '\\';

    private DbSet<MembershipPlan> Plans => context.Set<MembershipPlan>();

    public void Add(MembershipPlan plan) => Plans.Add(plan);

    public Task<MembershipPlan?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Plans
            .Include(plan => plan.Perks)
            .FirstOrDefaultAsync(plan => plan.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Plans.AsNoTracking().AnyAsync(
            plan => plan.Slug == slug && (exceptId == null || plan.Id != exceptId), cancellationToken);

    public async Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        [.. await Plans.AsNoTracking().Select(plan => plan.Slug).ToListAsync(cancellationToken)];

    public async Task<int> NextDisplayOrderAsync(CancellationToken cancellationToken) =>
        (await Plans.AsNoTracking().MaxAsync(plan => (int?)plan.DisplayOrder, cancellationToken) ?? 0) + 1;

    public Task<List<MembershipPlanSummaryServiceModel>> ListAsync(
        MembershipPlanListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = Plans.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(plan => plan.Status == wanted);
        }

        if (!string.IsNullOrEmpty(search))
        {
            // LIKE rather than Contains: case-insensitive on SQL Server's collation and on SQLite alike, with the
            // search's own wildcards escaped so "50%" finds a percent sign.
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(plan => EF.Functions.Like(plan.Name, pattern, LikeEscape.ToString()));
        }

        if (after is not null)
        {
            query = query.Where(plan => plan.DisplayOrder > after.DisplayOrder
                || (plan.DisplayOrder == after.DisplayOrder && plan.Id.CompareTo(after.Id) > 0));
        }

        return query
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.Id)
            .Take(take)
            .Select(plan => new MembershipPlanSummaryServiceModel(
                plan.Id,
                plan.Name,
                plan.Slug,
                plan.Status,
                plan.DiscountPercent,
                plan.Featured,
                plan.DisplayOrder,
                plan.Perks.Count,
                plan.StartingPriceCents,
                plan.PriceCents,
                plan.Cadence,
                CatalogPolicy.Currency,
                plan.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        await Plans.AsNoTracking()
            .Where(plan => plan.Status == PublishStatus.Scheduled
                && plan.ScheduledFor != null && plan.ScheduledFor <= asOf)
            .OrderBy(plan => plan.ScheduledFor)
            .ThenBy(plan => plan.Id)
            .Take(take)
            .Select(plan => plan.Id)
            .ToListAsync(cancellationToken);

    public Task<List<MembershipPlan>> ListPublishedAsync(CancellationToken cancellationToken) =>
        Plans.AsNoTracking()
            .Include(plan => plan.Perks)
            .Where(plan => plan.Status == PublishStatus.Published)
            .OrderBy(plan => plan.DisplayOrder)
            .ThenBy(plan => plan.Name)
            .ThenBy(plan => plan.Id)
            .ToListAsync(cancellationToken);

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
