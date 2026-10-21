using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Data.Repositories;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Pages.Data;

/// <summary>
/// The Pages module's DataLayer: every write is one <c>SaveChangesAsync</c> — the page, its image references and its
/// audit row together. It stamps the bookkeeping (who, when, a new row version) and decides nothing itself.
/// </summary>
/// <remarks>
/// A slug change on a published record stages <c>redirects.slug-changed</c> in its save (ADR 0035), and every
/// status change stages <c>site.revalidate-requested</c> in its own (ADR 0036). Both are staged before the single
/// save, so the write and the message commit together or neither does (outbox.md).
/// </remarks>
internal sealed class PagesDataLayer(
    IPagesRepository repository,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IPagesDataLayer
{
    public Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.SlugExistsAsync(slug, exceptId, cancellationToken);

    public Task<List<PageSummaryServiceModel>> ListAsync(
        PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        repository.ListAsync(after, take, status, search, cancellationToken);

    public async Task<PageWriteOutcome> CreateAsync(Page page, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var now = clock.UtcNow;
        page.CreatedAt = now;
        Stamp(page, actorUserId, now);
        repository.Add(page);

        audit.Record(new AuditEntry(
            actorUserId, PageAudit.Created, PageAudit.ResourceType, page.Id.ToString(), correlation.Current,
            summary, AfterReference: $"slug:{page.Slug}"));

        return await SaveAsync(page, cancellationToken);
    }

    public async Task<PageWriteOutcome> UpdateAsync(
        Page page, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Stamp(page, actorUserId, clock.UtcNow);

        // Staged before the one save, so the rename and its redirect commit together or not at all (outbox.md).
        if (pathChange is not null)
        {
            outbox.Enqueue(
                RedirectSlugChanged.MessageType,
                new RedirectSlugChanged("page", page.Id, pathChange.FromPath, pathChange.ToPath, page.UpdatedAt).Serialize(),
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
        if (page.Status == PublishStatus.Published)
        {
            StageRevalidation(page);
        }

        audit.Record(new AuditEntry(
            actorUserId, PageAudit.Updated, PageAudit.ResourceType, page.Id.ToString(), correlation.Current, summary));

        return await SaveAsync(page, cancellationToken);
    }

    public async Task<PageWriteOutcome> TransitionAsync(
        Page page, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Stamp(page, actorUserId, clock.UtcNow);
        StageRevalidation(page);
        audit.Record(new AuditEntry(
            actorUserId, PageAudit.For(command), PageAudit.ResourceType, page.Id.ToString(), correlation.Current,
            $"{from} → {page.Status}.", BeforeReference: $"status:{from}", AfterReference: $"status:{page.Status}"));

        return await SaveAsync(page, cancellationToken);
    }

    public async Task<PageWriteOutcome> AbandonScheduleAsync(
        Page page, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        Stamp(page, ScheduledPublishPolicy.Actor, clock.UtcNow);
        StageRevalidation(page);

        // No actor: nobody did this, the schedule ran out (AuditLog.ActorUserId, as the seeders do).
        audit.Record(new AuditEntry(
            null, PageAudit.ScheduleAbandoned, PageAudit.ResourceType, page.Id.ToString(), correlation.Current,
            $"Scheduled publish abandoned: {reason}",
            BeforeReference: $"status:{from}", AfterReference: $"status:{page.Status}"));

        return await SaveAsync(page, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        repository.ListDueAsync(asOf, take, cancellationToken);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        repository.GetSlugsAsync(cancellationToken);

    public async Task<int> AddSeedAsync(IReadOnlyList<Page> pages, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pages);

        var now = clock.UtcNow;
        foreach (var page in pages)
        {
            page.CreatedAt = now;
            Stamp(page, actor, now);
            repository.Add(page);
        }

        // A system action: no user to name, so no actor (AuditLog.ActorUserId).
        audit.Record(new AuditEntry(
            null, PageAudit.Seeded, PageAudit.ResourceType, "placeholder-seed", correlation.Current,
            $"Seeded {pages.Count} placeholder pages: {string.Join(", ", pages.Select(page => page.Slug))}."));

        await repository.SaveChangesAsync(cancellationToken);
        return pages.Count;
    }

    /// <summary>
    /// Stages <c>site.revalidate-requested</c> for the page's own address and, through the type tag, anything else
    /// derived from pages (ADR 0036). Staged before the one save, so the status change and the invalidation commit
    /// together or not at all (outbox.md).
    /// </summary>
    private void StageRevalidation(Page page) =>
        outbox.Enqueue(
            SiteRevalidateRequested.MessageType,
            new SiteRevalidateRequested(
                PageAudit.ResourceType, page.Id, [SitePaths.AddressOf(PagePolicy.SitePathPrefix, page.Slug)]).Serialize(),
            correlation.Current);

    /// <summary>Who and when, and a new row version so every other open form's If-Match goes stale.</summary>
    private static void Stamp(Page page, string actorUserId, DateTimeOffset now)
    {
        page.UpdatedAt = now;
        page.UpdatedBy = actorUserId;
        page.RowVersion = Guid.NewGuid();
    }

    private async Task<PageWriteOutcome> SaveAsync(Page page, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return PageWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return PageWriteOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            // Most likely the unique slug index: another page took it after Business checked. Asked again rather than
            // assumed, so a genuine fault (an image deleted in the instant since Media was asked) is not reported as a
            // taken slug.
            repository.DiscardChanges();
            if (await repository.SlugExistsAsync(page.Slug, page.Id, cancellationToken))
            {
                return PageWriteOutcome.SlugTaken;
            }

            throw;
        }
    }
}
