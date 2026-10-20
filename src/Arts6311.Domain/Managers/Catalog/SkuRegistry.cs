using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Managers.Catalog;

internal sealed class SkuRegistry(ArtsDbContext context, IClock clock) : ISkuRegistry
{
    private DbSet<CatalogSku> Skus => context.Set<CatalogSku>();

    public async Task<IReadOnlyDictionary<string, SkuOwner>> OwnersAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skus);

        if (skus.Count == 0)
        {
            return new Dictionary<string, SkuOwner>(StringComparer.Ordinal);
        }

        var wanted = skus.Distinct(StringComparer.Ordinal).ToList();
        var rows = await Skus.AsNoTracking()
            .Where(row => wanted.Contains(row.Sku))
            .Select(row => new { row.Sku, row.OwnerType, row.OwnerId })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Sku, row => new SkuOwner(row.OwnerType, row.OwnerId), StringComparer.Ordinal);
    }

    public Task<bool> IsTakenAsync(string sku, SkuOwner owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return Skus.AsNoTracking().AnyAsync(
            row => row.Sku == sku && (row.OwnerType != owner.OwnerType || row.OwnerId != owner.OwnerId),
            cancellationToken);
    }

    public void Reserve(string sku, string ownerType, Guid ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerType);

        if (!SkuPolicy.IsValid(sku))
        {
            throw new ArgumentException($"“{sku}” is not a SKU this registry stores.", nameof(sku));
        }

        Skus.Add(new CatalogSku
        {
            Sku = sku,
            OwnerType = ownerType,
            OwnerId = ownerId,
            CreatedAt = clock.UtcNow,
        });
    }

    public async Task ReleaseAsync(string sku, SkuOwner owner, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentNullException.ThrowIfNull(owner);

        var tracked = context.ChangeTracker.Entries<CatalogSku>()
            .FirstOrDefault(entry => string.Equals(entry.Entity.Sku, sku, StringComparison.Ordinal));

        if (tracked is not null)
        {
            // Reserved and released in the same save (a rename back to what it was): the two cancel out.
            if (tracked.State == EntityState.Added)
            {
                tracked.State = EntityState.Detached;
            }
            else if (Holds(tracked.Entity, owner))
            {
                tracked.State = EntityState.Deleted;
            }

            return;
        }

        // Read before staging: deleting a row that is gone fails the whole save as a concurrency conflict, and the
        // caller would read that as "someone else saved first" rather than as the registry drift it is.
        var row = await Skus.FirstOrDefaultAsync(candidate => candidate.Sku == sku, cancellationToken);
        if (row is not null && Holds(row, owner))
        {
            Skus.Remove(row);
        }
    }

    private static bool Holds(CatalogSku row, SkuOwner owner) =>
        string.Equals(row.OwnerType, owner.OwnerType, StringComparison.Ordinal) && row.OwnerId == owner.OwnerId;
}
