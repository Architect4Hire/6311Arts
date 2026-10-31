using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Data.Repositories;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Instructors.Data;

/// <summary>
/// The Instructors module's DataLayer: every write is one <c>SaveChangesAsync</c>, with its audit row staged before it.
/// It stamps the bookkeeping (who, when, a new row version) and decides nothing about instructors itself.
/// </summary>
/// <remarks>
/// A slug change on a published record stages <c>redirects.slug-changed</c> in its save (ADR 0035), and every
/// status change stages <c>site.revalidate-requested</c> in its own (ADR 0036). Both are staged before the single
/// save, so the write and the message commit together or neither does (outbox.md).
/// </remarks>
internal sealed class InstructorsDataLayer(
    IInstructorsRepository repository,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IInstructorsDataLayer
{
    public Task<Instructor?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public Task<Instructor?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindDeletedAsync(id, cancellationToken);

    public Task<List<string>> ListNamingArtistAsync(Guid artistId, int take, CancellationToken cancellationToken) =>
        repository.ListNamingArtistAsync(artistId, take, cancellationToken);

    public Task<List<InstructorDeletedRow>> ListDeletedAsync(
        InstructorListPosition? after, int take, string? search, CancellationToken cancellationToken) =>
        repository.ListDeletedAsync(after, take, search, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.SlugExistsAsync(slug, exceptId, cancellationToken);

    public Task<List<InstructorSummaryServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        repository.ListAsync(sort, after, take, status, search, cancellationToken);

    public Task<List<InstructorSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        repository.ListByIdsAsync(ids, cancellationToken);

    public Task<List<InstructorSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        repository.ListBySlugsAsync(slugs, cancellationToken);

    public Task<List<InstructorSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken) =>
        repository.ListPublishedAsync(slug, cancellationToken);

    public async Task<InstructorWriteOutcome> CreateAsync(Instructor instructor, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructor);

        var now = clock.UtcNow;
        instructor.CreatedAt = now;
        Stamp(instructor, actorUserId, now);
        repository.Add(instructor);

        audit.Record(new AuditEntry(
            actorUserId, InstructorAudit.Created, InstructorAudit.ResourceType, instructor.Id.ToString(), correlation.Current,
            $"Created as {instructor.Status}.", AfterReference: $"slug:{instructor.Slug}"));

        return await SaveAsync(instructor, cancellationToken);
    }

    public async Task<InstructorWriteOutcome> UpdateAsync(
        Instructor instructor, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructor);

        Stamp(instructor, actorUserId, clock.UtcNow);

        // Staged before the one save, so the rename and its redirect commit together or not at all (outbox.md).
        if (pathChange is not null)
        {
            outbox.Enqueue(
                RedirectSlugChanged.MessageType,
                new RedirectSlugChanged(InstructorAudit.ResourceType, instructor.Id, pathChange.FromPath, pathChange.ToPath, instructor.UpdatedAt).Serialize(),
                correlation.Current);
        }

        // An edit to a record the site is already showing has to invalidate what shows it, exactly as a status
        // change does. Staged before the one save, so the edit and the invalidation commit together or neither
        // does (outbox.md).
        //
        // Whatever the instructor's status, as for an artist: an instructor who is not published is still named on
        // published classes (ADR 0043, 0044), so a draft instructor's rename changes what the site shows too, and the
        // "instructor" type tag on those entries is what clears them.
        StageRevalidation(instructor);

        audit.Record(new AuditEntry(
            actorUserId, InstructorAudit.Updated, InstructorAudit.ResourceType, instructor.Id.ToString(), correlation.Current, summary));

        return await SaveAsync(instructor, cancellationToken);
    }

    public async Task<InstructorWriteOutcome> TransitionAsync(
        Instructor instructor, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructor);

        Stamp(instructor, actorUserId, clock.UtcNow);
        StageRevalidation(instructor);
        audit.Record(new AuditEntry(
            actorUserId, InstructorAudit.For(command), InstructorAudit.ResourceType, instructor.Id.ToString(), correlation.Current,
            $"{from} → {instructor.Status}.", BeforeReference: $"status:{from}", AfterReference: $"status:{instructor.Status}"));

        return await SaveAsync(instructor, cancellationToken);
    }

    public async Task<InstructorWriteOutcome> DeleteAsync(
        Instructor instructor, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructor);

        var now = clock.UtcNow;
        var deletedAs = instructor.Status;
        SoftDelete.Stamp(instructor, actorUserId, now);
        Stamp(instructor, actorUserId, now);

        // Whatever its status, as every write to an instructor is: an unpublished instructor is still named on published
        // classes (ADR 0042, 0043), so a delete changes what the site shows whatever the record was.
        StageRevalidation(instructor);

        audit.Record(new AuditEntry(
            actorUserId, InstructorAudit.Deleted, InstructorAudit.ResourceType, instructor.Id.ToString(), correlation.Current,
            $"Deleted while {deletedAs.ToString().ToLowerInvariant()}. Its web address is free again.",
            BeforeReference: $"status:{deletedAs}", AfterReference: $"slug:{instructor.Slug}"));

        return await SaveAsync(instructor, cancellationToken);
    }

    public async Task<InstructorWriteOutcome> RestoreAsync(
        Instructor instructor, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructor);

        SoftDelete.Clear(instructor);
        Stamp(instructor, actorUserId, clock.UtcNow);

        // The same revalidation as any other write to this type, for the reason the delete above gives.
        StageRevalidation(instructor);

        audit.Record(new AuditEntry(
            actorUserId, InstructorAudit.Restored, InstructorAudit.ResourceType, instructor.Id.ToString(), correlation.Current,
            $"Restored as draft; it was {deletedAs.ToString().ToLowerInvariant()} when it was deleted.",
            BeforeReference: $"status:{deletedAs}", AfterReference: $"status:{instructor.Status}"));

        return await SaveAsync(instructor, cancellationToken);
    }

    public async Task<InstructorWriteOutcome> AbandonScheduleAsync(
        Instructor instructor, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructor);

        Stamp(instructor, ScheduledPublishPolicy.Actor, clock.UtcNow);
        StageRevalidation(instructor);

        // No actor: nobody did this, the schedule ran out (AuditLog.ActorUserId, as the seeders do).
        audit.Record(new AuditEntry(
            null, InstructorAudit.ScheduleAbandoned, InstructorAudit.ResourceType, instructor.Id.ToString(), correlation.Current,
            $"Scheduled publish abandoned: {reason}", BeforeReference: $"status:{from}", AfterReference: $"status:{instructor.Status}"));

        return await SaveAsync(instructor, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        repository.ListDueAsync(asOf, take, cancellationToken);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        repository.GetSlugsAsync(cancellationToken);

    public async Task<int> AddSeedAsync(IReadOnlyList<Instructor> instructors, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructors);

        var now = clock.UtcNow;
        foreach (var instructor in instructors)
        {
            instructor.CreatedAt = now;
            Stamp(instructor, actor, now);
            repository.Add(instructor);
        }

        var added = instructors.Count;

        // A system action: no user to name, so no actor (AuditLog.ActorUserId).
        audit.Record(new AuditEntry(
            null, InstructorAudit.Seeded, InstructorAudit.ResourceType, "development-seed", correlation.Current,
            $"Seeded {added} development instructors."));

        await repository.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>
    /// Stages <c>site.revalidate-requested</c> for the instructor's own page and, through the type tag, every list
    /// that shows them (ADR 0036). Staged before the one save, so the status change and the invalidation commit
    /// together or not at all (outbox.md).
    /// </summary>
    private void StageRevalidation(Instructor instructor) =>
        outbox.Enqueue(
            SiteRevalidateRequested.MessageType,
            new SiteRevalidateRequested(
                InstructorAudit.ResourceType, instructor.Id, [SitePaths.AddressOf(InstructorPolicy.SitePathPrefix, instructor.Slug)]).Serialize(),
            correlation.Current);

    /// <summary>Who and when, and a new row version so every other open form's If-Match goes stale. EF still compares against the version the row was read with.</summary>
    private static void Stamp(Instructor instructor, string actorUserId, DateTimeOffset now)
    {
        instructor.UpdatedAt = now;
        instructor.UpdatedBy = actorUserId;
        instructor.RowVersion = Guid.NewGuid();
    }

    private async Task<InstructorWriteOutcome> SaveAsync(Instructor instructor, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return InstructorWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return InstructorWriteOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            // Most likely the unique slug index: another instructor took it after Business checked. Asked again rather
            // than assumed, so a genuine fault is not reported as a taken slug.
            repository.DiscardChanges();
            if (await repository.SlugExistsAsync(instructor.Slug, instructor.Id, cancellationToken))
            {
                return InstructorWriteOutcome.SlugTaken;
            }

            throw;
        }
    }
}
