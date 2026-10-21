using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Data.Repositories;
using Arts6311.Domain.Modules.Classes.Managers;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Classes.Data;

/// <summary>
/// The Classes module's DataLayer: every write is one <c>SaveChangesAsync</c> — the offering, its runs and their
/// sessions, its body images, its SKU registry rows and its audit row together. It stamps the bookkeeping (who, when, a new row
/// version) and decides nothing itself.
/// </summary>
/// <remarks>
/// A slug change on a published record stages <c>redirects.slug-changed</c> in its save (ADR 0035), and every status
/// change stages <c>site.revalidate-requested</c> in its own (ADR 0036). The SKU reservations go in the same save as
/// the runs that carry them, so a write that fails claims no SKU (ADR 0037, 0044). Everything is staged before the
/// single save, so each pair commits together or neither does (outbox.md).
/// </remarks>
internal sealed class ClassesDataLayer(
    IClassesRepository repository,
    ISkuRegistry skus,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IClassesDataLayer
{
    public Task<ClassOffering?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.SlugExistsAsync(slug, exceptId, cancellationToken);

    public Task<IReadOnlyDictionary<string, SkuOwner>> SkuOwnersAsync(
        IReadOnlyCollection<string> candidates, CancellationToken cancellationToken) =>
        skus.OwnersAsync(candidates, cancellationToken);

    public Task<int> NextOfferingNumberAsync(CancellationToken cancellationToken) =>
        repository.NextOfferingNumberAsync(cancellationToken);

    public Task<List<ClassListRow>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        repository.ListAsync(after, take, status, search, asOf, cancellationToken);

    public async Task<ClassWriteOutcome> CreateAsync(
        ClassOffering offering, SkuChanges skuChanges, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offering);

        var now = clock.UtcNow;
        offering.CreatedAt = now;
        Stamp(offering, actorUserId, now);
        repository.Add(offering);
        await StageSkusAsync(skuChanges, cancellationToken);

        audit.Record(new AuditEntry(
            actorUserId, ClassAudit.Created, ClassAudit.ResourceType, offering.Id.ToString(), correlation.Current,
            summary, AfterReference: $"slug:{offering.Slug}"));

        return await SaveAsync(offering, skuChanges, cancellationToken);
    }

    public async Task<ClassWriteOutcome> UpdateAsync(
        ClassOffering offering,
        SkuChanges skuChanges,
        string actorUserId,
        string summary,
        PublishedPathChange? pathChange,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offering);

        Stamp(offering, actorUserId, clock.UtcNow);
        await StageSkusAsync(skuChanges, cancellationToken);

        // Staged before the one save, so the rename and its redirect commit together or not at all (outbox.md).
        if (pathChange is not null)
        {
            outbox.Enqueue(
                RedirectSlugChanged.MessageType,
                new RedirectSlugChanged(
                    ClassAudit.ResourceType, offering.Id, pathChange.FromPath, pathChange.ToPath, offering.UpdatedAt).Serialize(),
                correlation.Current);
        }

        // An edit to a record the site is already showing has to invalidate what shows it, exactly as a status
        // change does. Staged before the one save, so the edit and the invalidation commit together or neither
        // does (outbox.md). Only while it is live: nothing public renders a Draft, Scheduled or Archived record,
        // so there is nothing cached to clear, and a draft edit should not spend an outbox row.
        //
        // ADR 0036 left this to 3.1 on the grounds that nothing caches a record's content yet. Closed here
        // instead: the machinery is this module's own StageRevalidation, and leaving it out means every content
        // edit silently serves stale copy from the moment 3.1 turns caching on (architecture + outbox review).
        if (offering.Status == PublishStatus.Published)
        {
            StageRevalidation(offering);
        }

        audit.Record(new AuditEntry(
            actorUserId, ClassAudit.Updated, ClassAudit.ResourceType, offering.Id.ToString(), correlation.Current, summary));

        return await SaveAsync(offering, skuChanges, cancellationToken);
    }

    public async Task<ClassWriteOutcome> TransitionAsync(
        ClassOffering offering, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offering);

        Stamp(offering, actorUserId, clock.UtcNow);
        StageRevalidation(offering);
        audit.Record(new AuditEntry(
            actorUserId, ClassAudit.For(command), ClassAudit.ResourceType, offering.Id.ToString(),
            correlation.Current, $"{from} → {offering.Status}.",
            BeforeReference: $"status:{from}", AfterReference: $"status:{offering.Status}"));

        return await SaveAsync(offering, SkuChanges.None, cancellationToken);
    }

    public async Task<ClassWriteOutcome> AbandonScheduleAsync(
        ClassOffering offering, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offering);

        Stamp(offering, ScheduledPublishPolicy.Actor, clock.UtcNow);
        StageRevalidation(offering);

        // No actor: nobody did this, the schedule ran out (AuditLog.ActorUserId, as the seeders do).
        audit.Record(new AuditEntry(
            null, ClassAudit.ScheduleAbandoned, ClassAudit.ResourceType, offering.Id.ToString(),
            correlation.Current, $"Scheduled publish abandoned: {reason}",
            BeforeReference: $"status:{from}", AfterReference: $"status:{offering.Status}"));

        return await SaveAsync(offering, SkuChanges.None, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        repository.ListDueAsync(asOf, take, cancellationToken);

    public Task<List<ClassOffering>> ListPublishedAsync(
        string? slug, DateTimeOffset? endingAfter, CancellationToken cancellationToken) =>
        repository.ListPublishedAsync(slug, endingAfter, cancellationToken);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        repository.GetSlugsAsync(cancellationToken);

    public async Task<int> AddSeedAsync(
        IReadOnlyList<ClassOffering> offerings, SkuChanges skuChanges, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offerings);

        var now = clock.UtcNow;
        foreach (var offering in offerings)
        {
            offering.CreatedAt = now;
            Stamp(offering, actor, now);
            repository.Add(offering);
        }

        await StageSkusAsync(skuChanges, cancellationToken);
        var added = offerings.Count;

        // A system action: no user to name, so no actor (AuditLog.ActorUserId).
        audit.Record(new AuditEntry(
            null, ClassAudit.Seeded, ClassAudit.ResourceType, "development-seed", correlation.Current,
            $"Seeded {added} development class offerings."));

        await repository.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>
    /// The registry rows for the SKUs this write hands over: releases first, so a SKU moving from one run to another
    /// inside the same offering is free by the time it is reserved again (ADR 0037).
    /// </summary>
    private async Task StageSkusAsync(SkuChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var released in changes.Release)
        {
            await skus.ReleaseAsync(
                released.Sku,
                new SkuOwner(CatalogOwnerTypes.ClassRun, released.OwnerId),
                cancellationToken);
        }

        foreach (var reservation in changes.Reserve)
        {
            skus.Reserve(reservation.Sku, CatalogOwnerTypes.ClassRun, reservation.OwnerId);
        }
    }

    /// <summary>
    /// Stages <c>site.revalidate-requested</c> for the offering's own page and, through the type tag, every list
    /// that shows it (ADR 0036). Staged before the one save, so the status change and the invalidation commit
    /// together or not at all (outbox.md).
    /// </summary>
    private void StageRevalidation(ClassOffering offering) =>
        outbox.Enqueue(
            SiteRevalidateRequested.MessageType,
            new SiteRevalidateRequested(
                ClassAudit.ResourceType, offering.Id,
                [SitePaths.AddressOf(ClassPolicy.SitePathPrefix, offering.Slug)]).Serialize(),
            correlation.Current);

    /// <summary>
    /// Who and when, and a new row version so every other open form's <c>If-Match</c> goes stale — a change to the
    /// runs or sessions alone included. EF still compares against the version the row was read with.
    /// </summary>
    private static void Stamp(ClassOffering offering, string actorUserId, DateTimeOffset now)
    {
        offering.UpdatedAt = now;
        offering.UpdatedBy = actorUserId;
        offering.RowVersion = Guid.NewGuid();
    }

    private async Task<ClassWriteOutcome> SaveAsync(
        ClassOffering offering, SkuChanges skuChanges, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return ClassWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return ClassWriteOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            // A unique index refused the insert: the slug, or a SKU another record took after Business checked.
            // Asked again rather than assumed, so a genuine fault is not reported as a clash.
            repository.DiscardChanges();

            if (await repository.SlugExistsAsync(offering.Slug, offering.Id, cancellationToken))
            {
                return ClassWriteOutcome.SlugTaken;
            }

            foreach (var reservation in skuChanges.Reserve)
            {
                if (await skus.IsTakenAsync(
                    reservation.Sku,
                    new SkuOwner(CatalogOwnerTypes.ClassRun, reservation.OwnerId),
                    cancellationToken))
                {
                    return ClassWriteOutcome.SkuTaken;
                }
            }

            throw;
        }
    }
}
