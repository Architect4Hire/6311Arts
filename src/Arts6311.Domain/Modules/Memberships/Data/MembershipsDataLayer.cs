using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Data.Repositories;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Memberships.Data;

/// <summary>
/// The Memberships module's DataLayer: every write is one <c>SaveChangesAsync</c> — the plan, its perks and its
/// audit row together. It stamps the bookkeeping (who, when, a new row version) and decides nothing itself.
/// </summary>
/// <remarks>
/// Every status change stages <c>site.revalidate-requested</c> in its own save (ADR 0036), naming the one page
/// every plan shares. Nothing stages <c>redirects.slug-changed</c>: a plan has no address of its own, so a renamed
/// slug leaves no old address to redirect (ADR 0040). Everything is staged before the single save, so the write
/// and its message commit together or neither does (outbox.md).
/// </remarks>
internal sealed class MembershipsDataLayer(
    IMembershipsRepository repository,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IMembershipsDataLayer
{
    public Task<MembershipPlan?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public Task<MembershipPlan?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindDeletedAsync(id, cancellationToken);

    public Task<List<MembershipPlanDeletedRow>> ListDeletedAsync(
        MembershipPlanListPosition? after, int take, string? search, CancellationToken cancellationToken) =>
        repository.ListDeletedAsync(after, take, search, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.SlugExistsAsync(slug, exceptId, cancellationToken);

    public Task<int> NextDisplayOrderAsync(CancellationToken cancellationToken) =>
        repository.NextDisplayOrderAsync(cancellationToken);

    public Task<List<MembershipPlanSummaryServiceModel>> ListAsync(
        ListSort sort,
        ListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        CancellationToken cancellationToken) =>
        repository.ListAsync(sort, after, take, status, search, cancellationToken);

    public async Task<MembershipWriteOutcome> CreateAsync(
        MembershipPlan plan, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var now = clock.UtcNow;
        plan.CreatedAt = now;
        Stamp(plan, actorUserId, now);
        repository.Add(plan);

        audit.Record(new AuditEntry(
            actorUserId, MembershipAudit.Created, MembershipAudit.ResourceType, plan.Id.ToString(),
            correlation.Current, summary, AfterReference: $"slug:{plan.Slug}"));

        return await SaveAsync(plan, cancellationToken);
    }

    public async Task<MembershipWriteOutcome> UpdateAsync(
        MembershipPlan plan, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Stamp(plan, actorUserId, clock.UtcNow);

        // An edit to a record the site is already showing has to invalidate what shows it, exactly as a status
        // change does. Staged before the one save, so the edit and the invalidation commit together or neither
        // does (outbox.md). Only while it is live: nothing public renders a Draft, Scheduled or Archived record,
        // so there is nothing cached to clear, and a draft edit should not spend an outbox row.
        //
        // ADR 0036 left this to 3.1 on the grounds that nothing caches a record's content yet. Closed here
        // instead: the machinery is this module's own StageRevalidation, and leaving it out means every content
        // edit silently serves stale copy from the moment 3.1 turns caching on (architecture + outbox review).
        if (plan.Status == PublishStatus.Published)
        {
            StageRevalidation(plan);
        }

        audit.Record(new AuditEntry(
            actorUserId, MembershipAudit.Updated, MembershipAudit.ResourceType, plan.Id.ToString(),
            correlation.Current, summary));

        return await SaveAsync(plan, cancellationToken);
    }

    public async Task<MembershipWriteOutcome> TransitionAsync(
        MembershipPlan plan,
        PublishCommand command,
        PublishStatus from,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Stamp(plan, actorUserId, clock.UtcNow);
        StageRevalidation(plan);
        audit.Record(new AuditEntry(
            actorUserId, MembershipAudit.For(command), MembershipAudit.ResourceType, plan.Id.ToString(),
            correlation.Current, $"{from} → {plan.Status}.",
            BeforeReference: $"status:{from}", AfterReference: $"status:{plan.Status}"));

        return await SaveAsync(plan, cancellationToken);
    }

    public async Task<MembershipWriteOutcome> DeleteAsync(
        MembershipPlan plan, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var now = clock.UtcNow;
        var deletedAs = plan.Status;
        SoftDelete.Stamp(plan, actorUserId, now);
        Stamp(plan, actorUserId, now);

        // Only while the site was showing it. A Draft or Scheduled record is rendered nowhere, and an Archived one
        // had its revalidation when it was archived, so there is nothing cached to clear (content.md, ADR 0036).
        if (deletedAs == PublishStatus.Published)
        {
            StageRevalidation(plan);
        }

        audit.Record(new AuditEntry(
            actorUserId, MembershipAudit.Deleted, MembershipAudit.ResourceType, plan.Id.ToString(), correlation.Current,
            $"Deleted while {deletedAs.ToString().ToLowerInvariant()}. Its web address is free again.",
            BeforeReference: $"status:{deletedAs}", AfterReference: $"slug:{plan.Slug}"));

        return await SaveAsync(plan, cancellationToken);
    }

    public async Task<MembershipWriteOutcome> RestoreAsync(
        MembershipPlan plan, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        SoftDelete.Clear(plan);
        Stamp(plan, actorUserId, clock.UtcNow);
        // Nothing is revalidated: Business brings a record back as a Draft, and nothing public renders one.
        audit.Record(new AuditEntry(
            actorUserId, MembershipAudit.Restored, MembershipAudit.ResourceType, plan.Id.ToString(), correlation.Current,
            $"Restored as draft; it was {deletedAs.ToString().ToLowerInvariant()} when it was deleted.",
            BeforeReference: $"status:{deletedAs}", AfterReference: $"status:{plan.Status}"));

        return await SaveAsync(plan, cancellationToken);
    }

    public async Task<MembershipWriteOutcome> AbandonScheduleAsync(
        MembershipPlan plan, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Stamp(plan, ScheduledPublishPolicy.Actor, clock.UtcNow);
        StageRevalidation(plan);

        // No actor: nobody did this, the schedule ran out (AuditLog.ActorUserId, as the seeders do).
        audit.Record(new AuditEntry(
            null, MembershipAudit.ScheduleAbandoned, MembershipAudit.ResourceType, plan.Id.ToString(),
            correlation.Current, $"Scheduled publish abandoned: {reason}",
            BeforeReference: $"status:{from}", AfterReference: $"status:{plan.Status}"));

        return await SaveAsync(plan, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        repository.ListDueAsync(asOf, take, cancellationToken);

    public Task<List<MembershipPlan>> ListPublishedAsync(CancellationToken cancellationToken) =>
        repository.ListPublishedAsync(cancellationToken);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        repository.GetSlugsAsync(cancellationToken);

    public async Task<int> AddSeedAsync(
        IReadOnlyList<MembershipPlan> plans, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var now = clock.UtcNow;
        foreach (var plan in plans)
        {
            plan.CreatedAt = now;
            Stamp(plan, actor, now);
            repository.Add(plan);
        }

        var added = plans.Count;

        // A system action: no user to name, so no actor (AuditLog.ActorUserId).
        audit.Record(new AuditEntry(
            null, MembershipAudit.Seeded, MembershipAudit.ResourceType, "development-seed", correlation.Current,
            $"Seeded {added} development membership plans."));

        await repository.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>
    /// Stages <c>site.revalidate-requested</c> for the memberships page — the one address every plan shares
    /// (ADR 0040) — and, through the type and entity tags, anything else derived from this plan (ADR 0036). Staged
    /// before the one save, so the status change and the invalidation commit together or not at all (outbox.md).
    /// </summary>
    private void StageRevalidation(MembershipPlan plan) =>
        outbox.Enqueue(
            SiteRevalidateRequested.MessageType,
            new SiteRevalidateRequested(
                MembershipAudit.ResourceType, plan.Id, [MembershipPolicy.SitePath]).Serialize(),
            correlation.Current);

    /// <summary>
    /// Who and when, and a new row version so every other open form's <c>If-Match</c> goes stale — a change to the
    /// perks alone included.
    /// </summary>
    private static void Stamp(MembershipPlan plan, string actorUserId, DateTimeOffset now)
    {
        plan.UpdatedAt = now;
        plan.UpdatedBy = actorUserId;
        plan.RowVersion = Guid.NewGuid();
    }

    private async Task<MembershipWriteOutcome> SaveAsync(MembershipPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return MembershipWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return MembershipWriteOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            // A unique index refused the write: the slug another plan took after Business checked. Asked again
            // rather than assumed, so a genuine fault is not reported as a clash.
            repository.DiscardChanges();

            if (await repository.SlugExistsAsync(plan.Slug, plan.Id, cancellationToken))
            {
                return MembershipWriteOutcome.SlugTaken;
            }

            throw;
        }
    }
}
