using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Data.Repositories;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Artists.Data;

/// <summary>
/// The Artists module's DataLayer: every write is one <c>SaveChangesAsync</c>, with its audit row staged before it.
/// It stamps the bookkeeping (who, when, a new row version) and decides nothing about artists itself.
/// </summary>
/// <remarks>
/// A slug change on a published record stages <c>redirects.slug-changed</c> in its save (ADR 0035), and every
/// status change stages <c>site.revalidate-requested</c> in its own (ADR 0036). Both are staged before the single
/// save, so the write and the message commit together or neither does (outbox.md).
/// </remarks>
internal sealed class ArtistsDataLayer(
    IArtistsRepository repository,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IArtistsDataLayer
{
    public Task<Artist?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.SlugExistsAsync(slug, exceptId, cancellationToken);

    public Task<List<ArtistSummaryServiceModel>> ListAsync(
        ArtistListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken) =>
        repository.ListAsync(after, take, status, search, cancellationToken);

    public Task<List<ArtistSummaryServiceModel>> GetSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        repository.ListByIdsAsync(ids, cancellationToken);

    public Task<List<ArtistSummaryServiceModel>> FindBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        repository.ListBySlugsAsync(slugs, cancellationToken);

    public Task<List<ArtistSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken) =>
        repository.ListPublishedAsync(slug, cancellationToken);

    public async Task<ArtistWriteOutcome> CreateAsync(Artist artist, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artist);

        var now = clock.UtcNow;
        artist.CreatedAt = now;
        Stamp(artist, actorUserId, now);
        repository.Add(artist);

        audit.Record(new AuditEntry(
            actorUserId, ArtistAudit.Created, ArtistAudit.ResourceType, artist.Id.ToString(), correlation.Current,
            $"Created as {artist.Status}.", AfterReference: $"slug:{artist.Slug}"));

        return await SaveAsync(artist, cancellationToken);
    }

    public async Task<ArtistWriteOutcome> UpdateAsync(
        Artist artist, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artist);

        Stamp(artist, actorUserId, clock.UtcNow);

        // Staged before the one save, so the rename and its redirect commit together or not at all (outbox.md).
        if (pathChange is not null)
        {
            outbox.Enqueue(
                RedirectSlugChanged.MessageType,
                new RedirectSlugChanged("artist", artist.Id, pathChange.FromPath, pathChange.ToPath, artist.UpdatedAt).Serialize(),
                correlation.Current);
        }

        // An edit to a record the site is already showing has to invalidate what shows it, exactly as a status
        // change does. Staged before the one save, so the edit and the invalidation commit together or neither
        // does (outbox.md).
        //
        // Whatever the artist's status, unlike every other type: an artist who is not published is still credited
        // by name on published exhibitions and artwork (ADR 0033, 0042), so a draft artist's rename changes what
        // the site shows too, and the "artist" type tag on those entries is what clears them.
        StageRevalidation(artist);

        audit.Record(new AuditEntry(
            actorUserId, ArtistAudit.Updated, ArtistAudit.ResourceType, artist.Id.ToString(), correlation.Current, summary));

        return await SaveAsync(artist, cancellationToken);
    }

    public async Task<ArtistWriteOutcome> TransitionAsync(
        Artist artist, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artist);

        Stamp(artist, actorUserId, clock.UtcNow);
        StageRevalidation(artist);
        audit.Record(new AuditEntry(
            actorUserId, ArtistAudit.For(command), ArtistAudit.ResourceType, artist.Id.ToString(), correlation.Current,
            $"{from} → {artist.Status}.", BeforeReference: $"status:{from}", AfterReference: $"status:{artist.Status}"));

        return await SaveAsync(artist, cancellationToken);
    }

    public async Task<ArtistWriteOutcome> AbandonScheduleAsync(
        Artist artist, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artist);

        Stamp(artist, ScheduledPublishPolicy.Actor, clock.UtcNow);
        StageRevalidation(artist);

        // No actor: nobody did this, the schedule ran out (AuditLog.ActorUserId, as the seeders do).
        audit.Record(new AuditEntry(
            null, ArtistAudit.ScheduleAbandoned, ArtistAudit.ResourceType, artist.Id.ToString(), correlation.Current,
            $"Scheduled publish abandoned: {reason}", BeforeReference: $"status:{from}", AfterReference: $"status:{artist.Status}"));

        return await SaveAsync(artist, cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        repository.ListDueAsync(asOf, take, cancellationToken);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        repository.GetSlugsAsync(cancellationToken);

    public async Task<int> AddSeedAsync(IReadOnlyList<Artist> artists, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artists);

        var now = clock.UtcNow;
        foreach (var artist in artists)
        {
            artist.CreatedAt = now;
            Stamp(artist, actor, now);
            repository.Add(artist);
        }

        var added = artists.Count;

        // A system action: no user to name, so no actor (AuditLog.ActorUserId).
        audit.Record(new AuditEntry(
            null, ArtistAudit.Seeded, ArtistAudit.ResourceType, "development-seed", correlation.Current,
            $"Seeded {added} development artists."));

        await repository.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <summary>
    /// Stages <c>site.revalidate-requested</c> for the artist's own page and, through the type tag, every list
    /// that shows them (ADR 0036). Staged before the one save, so the status change and the invalidation commit
    /// together or not at all (outbox.md).
    /// </summary>
    private void StageRevalidation(Artist artist) =>
        outbox.Enqueue(
            SiteRevalidateRequested.MessageType,
            new SiteRevalidateRequested(
                ArtistAudit.ResourceType, artist.Id, [SitePaths.AddressOf(ArtistPolicy.SitePathPrefix, artist.Slug)]).Serialize(),
            correlation.Current);

    /// <summary>Who and when, and a new row version so every other open form's If-Match goes stale. EF still compares against the version the row was read with.</summary>
    private static void Stamp(Artist artist, string actorUserId, DateTimeOffset now)
    {
        artist.UpdatedAt = now;
        artist.UpdatedBy = actorUserId;
        artist.RowVersion = Guid.NewGuid();
    }

    private async Task<ArtistWriteOutcome> SaveAsync(Artist artist, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return ArtistWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return ArtistWriteOutcome.Conflict;
        }
        catch (DbUpdateException)
        {
            // Most likely the unique slug index: another artist took it after Business checked. Asked again rather
            // than assumed, so a genuine fault is not reported as a taken slug.
            repository.DiscardChanges();
            if (await repository.SlugExistsAsync(artist.Slug, artist.Id, cancellationToken))
            {
                return ArtistWriteOutcome.SlugTaken;
            }

            throw;
        }
    }
}
