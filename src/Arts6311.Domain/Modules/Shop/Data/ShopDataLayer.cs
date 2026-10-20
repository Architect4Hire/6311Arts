using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Data.Repositories;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Shop.Data;

/// <summary>
/// The Shop module's DataLayer: every write is one <c>SaveChangesAsync</c> — the item, its categories, its gallery,
/// its description images, its SKU registry row and its audit row together. It stamps the bookkeeping (who, when, a
/// new row version) and decides nothing itself.
/// </summary>
/// <remarks>
/// A slug change on a published record stages <c>redirects.slug-changed</c> in its save (ADR 0035), and every status
/// change stages <c>site.revalidate-requested</c> in its own (ADR 0036). The SKU reservation goes in the same save
/// as the item that carries it, so a write that fails claims no SKU (ADR 0037). Everything is staged before the
/// single save, so each pair commits together or neither does (outbox.md).
/// </remarks>
internal sealed class ShopDataLayer(
    IShopRepository repository,
    ISkuRegistry skus,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IShopDataLayer
{
    public Task<ShopItem?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.SlugExistsAsync(slug, exceptId, cancellationToken);

    public Task<IReadOnlyDictionary<string, SkuOwner>> SkuOwnersAsync(
        IReadOnlyCollection<string> candidates, CancellationToken cancellationToken) =>
        skus.OwnersAsync(candidates, cancellationToken);

    public Task<int> NextItemNumberAsync(CancellationToken cancellationToken) =>
        repository.NextItemNumberAsync(cancellationToken);

    public Task<List<ShopItemListRow>> ListAsync(
        ShopItemListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        string? category,
        CancellationToken cancellationToken) =>
        repository.ListAsync(after, take, status, search, category, cancellationToken);

    public async Task<ShopWriteOutcome> CreateAsync(
        ShopItem item, SkuChanges skuChanges, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var now = clock.UtcNow;
        item.CreatedAt = now;
        Stamp(item, actorUserId, now);
        repository.Add(item);
        await StageSkusAsync(skuChanges, cancellationToken);

        audit.Record(new AuditEntry(
            actorUserId, ShopAudit.Created, ShopAudit.ResourceType, item.Id.ToString(), correlation.Current,
            summary, AfterReference: $"slug:{item.Slug}"));

        return await SaveAsync(item, skuChanges, cancellationToken);
    }

    public async Task<ShopWriteOutcome> UpdateAsync(
        ShopItem item,
        SkuChanges skuChanges,
        string actorUserId,
        string summary,
        PublishedPathChange? pathChange,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        Stamp(item, actorUserId, clock.UtcNow);
        await StageSkusAsync(skuChanges, cancellationToken);

        // Staged before the one save, so the rename and its redirect commit together or not at all (outbox.md).
        if (pathChange is not null)
        {
            outbox.Enqueue(
                RedirectSlugChanged.MessageType,
                new RedirectSlugChanged(
                    ShopAudit.ResourceType, item.Id, pathChange.FromPath, pathChange.ToPath, item.UpdatedAt).Serialize(),
                correlation.Current);
        }

        audit.Record(new AuditEntry(
            actorUserId, ShopAudit.Updated, ShopAudit.ResourceType, item.Id.ToString(), correlation.Current, summary));

        return await SaveAsync(item, skuChanges, cancellationToken);
    }

    public async Task<ShopWriteOutcome> TransitionAsync(
        ShopItem item, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        Stamp(item, actorUserId, clock.UtcNow);
        StageRevalidation(item);
        audit.Record(new AuditEntry(
            actorUserId, ShopAudit.For(command), ShopAudit.ResourceType, item.Id.ToString(),
            correlation.Current, $"{from} → {item.Status}.",
            BeforeReference: $"status:{from}", AfterReference: $"status:{item.Status}"));

        return await SaveAsync(item, SkuChanges.None, cancellationToken);
    }

    public async Task<ShopWriteOutcome> AbandonScheduleAsync(
        ShopItem item, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        Stamp(item, ScheduledPublishPolicy.Actor, clock.UtcNow);
        StageRevalidation(item);

        // No actor: nobody did this, the schedule ran out (AuditLog.ActorUserId, as the seeders do).
        audit.Record(new AuditEntry(
            null, ShopAudit.ScheduleAbandoned, ShopAudit.ResourceType, item.Id.ToString(),
            correlation.Current, $"Scheduled publish abandoned: {reason}",
            BeforeReference: $"status:{from}", AfterReference: $"status:{item.Status}"));

        return await SaveAsync(item, SkuChanges.None, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        repository.ListDueAsync(asOf, take, cancellationToken);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        repository.GetSlugsAsync(cancellationToken);

    public async Task<int> AddSeedAsync(
        IReadOnlyList<ShopItem> items, SkuChanges skuChanges, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        var now = clock.UtcNow;
        foreach (var item in items)
        {
            item.CreatedAt = now;
            Stamp(item, actor, now);
            repository.Add(item);
        }

        await StageSkusAsync(skuChanges, cancellationToken);
        var added = items.Count;

        // A system action: no user to name, so no actor (AuditLog.ActorUserId).
        audit.Record(new AuditEntry(
            null, ShopAudit.Seeded, ShopAudit.ResourceType, "development-seed", correlation.Current,
            $"Seeded {added} development shop items."));

        await repository.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>
    /// The registry rows for the SKU this write hands over: the release first, so a SKU this item is giving up and
    /// taking again in one save is free by the time it is reserved (ADR 0037).
    /// </summary>
    private async Task StageSkusAsync(SkuChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var released in changes.Release)
        {
            await skus.ReleaseAsync(
                released.Sku,
                new SkuOwner(CatalogOwnerTypes.ShopItem, released.OwnerId),
                cancellationToken);
        }

        foreach (var reservation in changes.Reserve)
        {
            skus.Reserve(reservation.Sku, CatalogOwnerTypes.ShopItem, reservation.OwnerId);
        }
    }

    /// <summary>
    /// Stages <c>site.revalidate-requested</c> for the item's own page and, through the type tag, every list that
    /// shows it (ADR 0036). Staged before the one save, so the status change and the invalidation commit together or
    /// not at all (outbox.md).
    /// </summary>
    private void StageRevalidation(ShopItem item) =>
        outbox.Enqueue(
            SiteRevalidateRequested.MessageType,
            new SiteRevalidateRequested(
                ShopAudit.ResourceType, item.Id,
                [SitePaths.AddressOf(ShopPolicy.SitePathPrefix, item.Slug)]).Serialize(),
            correlation.Current);

    /// <summary>
    /// Who and when, and a new row version so every other open form's <c>If-Match</c> goes stale — a change to the
    /// gallery or the categories alone included.
    /// </summary>
    private static void Stamp(ShopItem item, string actorUserId, DateTimeOffset now)
    {
        item.UpdatedAt = now;
        item.UpdatedBy = actorUserId;
        item.RowVersion = Guid.NewGuid();
    }

    private async Task<ShopWriteOutcome> SaveAsync(
        ShopItem item, SkuChanges skuChanges, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return ShopWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return ShopWriteOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            // A unique index refused the insert: the slug, or the SKU another record took after Business checked.
            // Asked again rather than assumed, so a genuine fault is not reported as a clash.
            repository.DiscardChanges();

            if (await repository.SlugExistsAsync(item.Slug, item.Id, cancellationToken))
            {
                return ShopWriteOutcome.SlugTaken;
            }

            foreach (var reservation in skuChanges.Reserve)
            {
                if (await skus.IsTakenAsync(
                    reservation.Sku,
                    new SkuOwner(CatalogOwnerTypes.ShopItem, reservation.OwnerId),
                    cancellationToken))
                {
                    return ShopWriteOutcome.SkuTaken;
                }
            }

            throw;
        }
    }
}
