using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Data.Repositories;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Exhibitions.Data;

/// <summary>
/// The Exhibitions module's DataLayer: every write is one <c>SaveChangesAsync</c> — the exhibition, its credits and
/// its audit row together. It stamps the bookkeeping (who, when, a new row version) and decides nothing itself.
/// </summary>
/// <remarks>
/// A slug change on a published record stages <c>redirects.slug-changed</c> in its save (ADR 0035), and every
/// status change stages <c>site.revalidate-requested</c> in its own (ADR 0036). Both are staged before the single
/// save, so the write and the message commit together or neither does (outbox.md).
/// </remarks>
internal sealed class ExhibitionsDataLayer(
    IExhibitionsRepository repository,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IExhibitionsDataLayer
{
    public Task<Exhibition?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public Task<Exhibition?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindDeletedAsync(id, cancellationToken);

    public Task<List<string>> ListNamingArtistAsync(Guid artistId, int take, CancellationToken cancellationToken) =>
        repository.ListNamingArtistAsync(artistId, take, cancellationToken);

    public Task<List<ExhibitionDeletedRow>> ListDeletedAsync(
        ExhibitionListPosition? after, int take, string? search, CancellationToken cancellationToken) =>
        repository.ListDeletedAsync(after, take, search, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.SlugExistsAsync(slug, exceptId, cancellationToken);

    public Task<List<ExhibitionSummaryServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        repository.ListAsync(sort, after, take, status, search, cancellationToken);

    public async Task<ExhibitionWriteOutcome> CreateAsync(
        Exhibition exhibition, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exhibition);

        var now = clock.UtcNow;
        exhibition.CreatedAt = now;
        Stamp(exhibition, actorUserId, now);
        repository.Add(exhibition);

        audit.Record(new AuditEntry(
            actorUserId, ExhibitionAudit.Created, ExhibitionAudit.ResourceType, exhibition.Id.ToString(), correlation.Current,
            $"Created as {exhibition.Status}.", AfterReference: $"slug:{exhibition.Slug}"));

        return await SaveAsync(exhibition, cancellationToken);
    }

    public async Task<ExhibitionWriteOutcome> UpdateAsync(
        Exhibition exhibition, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exhibition);

        Stamp(exhibition, actorUserId, clock.UtcNow);

        // Staged before the one save, so the rename and its redirect commit together or not at all (outbox.md).
        if (pathChange is not null)
        {
            outbox.Enqueue(
                RedirectSlugChanged.MessageType,
                new RedirectSlugChanged("exhibition", exhibition.Id, pathChange.FromPath, pathChange.ToPath, exhibition.UpdatedAt).Serialize(),
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
        if (exhibition.Status == PublishStatus.Published)
        {
            StageRevalidation(exhibition);
        }

        audit.Record(new AuditEntry(
            actorUserId, ExhibitionAudit.Updated, ExhibitionAudit.ResourceType, exhibition.Id.ToString(), correlation.Current,
            summary));

        return await SaveAsync(exhibition, cancellationToken);
    }

    public async Task<ExhibitionWriteOutcome> TransitionAsync(
        Exhibition exhibition, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exhibition);

        Stamp(exhibition, actorUserId, clock.UtcNow);
        StageRevalidation(exhibition);
        audit.Record(new AuditEntry(
            actorUserId, ExhibitionAudit.For(command), ExhibitionAudit.ResourceType, exhibition.Id.ToString(),
            correlation.Current, $"{from} → {exhibition.Status}.",
            BeforeReference: $"status:{from}", AfterReference: $"status:{exhibition.Status}"));

        return await SaveAsync(exhibition, cancellationToken);
    }

    public async Task<ExhibitionWriteOutcome> DeleteAsync(
        Exhibition exhibition, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exhibition);

        var now = clock.UtcNow;
        var deletedAs = exhibition.Status;
        SoftDelete.Stamp(exhibition, actorUserId, now);
        Stamp(exhibition, actorUserId, now);

        // Only while the site was showing it. A Draft or Scheduled record is rendered nowhere, and an Archived one
        // had its revalidation when it was archived, so there is nothing cached to clear (content.md, ADR 0036).
        if (deletedAs == PublishStatus.Published)
        {
            StageRevalidation(exhibition);
        }

        audit.Record(new AuditEntry(
            actorUserId, ExhibitionAudit.Deleted, ExhibitionAudit.ResourceType, exhibition.Id.ToString(), correlation.Current,
            $"Deleted while {deletedAs.ToString().ToLowerInvariant()}. Its web address is free again.",
            BeforeReference: $"status:{deletedAs}", AfterReference: $"slug:{exhibition.Slug}"));

        return await SaveAsync(exhibition, cancellationToken);
    }

    public async Task<ExhibitionWriteOutcome> RestoreAsync(
        Exhibition exhibition, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exhibition);

        SoftDelete.Clear(exhibition);
        Stamp(exhibition, actorUserId, clock.UtcNow);
        // Nothing is revalidated: Business brings a record back as a Draft, and nothing public renders one.
        audit.Record(new AuditEntry(
            actorUserId, ExhibitionAudit.Restored, ExhibitionAudit.ResourceType, exhibition.Id.ToString(), correlation.Current,
            $"Restored as draft; it was {deletedAs.ToString().ToLowerInvariant()} when it was deleted.",
            BeforeReference: $"status:{deletedAs}", AfterReference: $"status:{exhibition.Status}"));

        return await SaveAsync(exhibition, cancellationToken);
    }

    public async Task<ExhibitionWriteOutcome> AbandonScheduleAsync(
        Exhibition exhibition, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exhibition);

        Stamp(exhibition, ScheduledPublishPolicy.Actor, clock.UtcNow);
        StageRevalidation(exhibition);

        // No actor: nobody did this, the schedule ran out (AuditLog.ActorUserId, as the seeders do).
        audit.Record(new AuditEntry(
            null, ExhibitionAudit.ScheduleAbandoned, ExhibitionAudit.ResourceType, exhibition.Id.ToString(),
            correlation.Current, $"Scheduled publish abandoned: {reason}",
            BeforeReference: $"status:{from}", AfterReference: $"status:{exhibition.Status}"));

        return await SaveAsync(exhibition, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        repository.ListDueAsync(asOf, take, cancellationToken);

    public Task<List<Exhibition>> ListPublishedAsync(string? slug, CancellationToken cancellationToken) =>
        repository.ListPublishedAsync(slug, cancellationToken);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        repository.GetSlugsAsync(cancellationToken);

    public async Task<int> AddSeedAsync(IReadOnlyList<Exhibition> exhibitions, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exhibitions);

        var now = clock.UtcNow;
        foreach (var exhibition in exhibitions)
        {
            exhibition.CreatedAt = now;
            Stamp(exhibition, actor, now);
            repository.Add(exhibition);
        }

        var added = exhibitions.Count;

        // A system action: no user to name, so no actor (AuditLog.ActorUserId).
        audit.Record(new AuditEntry(
            null, ExhibitionAudit.Seeded, ExhibitionAudit.ResourceType, "development-seed", correlation.Current,
            $"Seeded {added} development exhibitions."));

        await repository.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>
    /// Stages <c>site.revalidate-requested</c> for the exhibition's own page and, through the type tag, every list
    /// that shows it (ADR 0036). Staged before the one save, so the status change and the invalidation commit
    /// together or not at all (outbox.md).
    /// </summary>
    private void StageRevalidation(Exhibition exhibition) =>
        outbox.Enqueue(
            SiteRevalidateRequested.MessageType,
            new SiteRevalidateRequested(
                ExhibitionAudit.ResourceType, exhibition.Id,
                [SitePaths.AddressOf(ExhibitionPolicy.SitePathPrefix, exhibition.Slug)]).Serialize(),
            correlation.Current);

    /// <summary>
    /// Who and when, and a new row version so every other open form's If-Match goes stale — a change to the credits
    /// alone included. EF still compares against the version the row was read with.
    /// </summary>
    private static void Stamp(Exhibition exhibition, string actorUserId, DateTimeOffset now)
    {
        exhibition.UpdatedAt = now;
        exhibition.UpdatedBy = actorUserId;
        exhibition.RowVersion = Guid.NewGuid();
    }

    private async Task<ExhibitionWriteOutcome> SaveAsync(Exhibition exhibition, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return ExhibitionWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return ExhibitionWriteOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            // Most likely the unique slug index: another exhibition took it after Business checked. Asked again rather
            // than assumed, so a genuine fault is not reported as a taken slug.
            repository.DiscardChanges();
            if (await repository.SlugExistsAsync(exhibition.Slug, exhibition.Id, cancellationToken))
            {
                return ExhibitionWriteOutcome.SlugTaken;
            }

            throw;
        }
    }
}
