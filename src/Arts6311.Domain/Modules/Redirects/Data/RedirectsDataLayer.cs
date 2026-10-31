using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Data.Repositories;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Redirects.Data;

/// <summary>
/// The Redirects module's DataLayer: every write is one <c>SaveChangesAsync</c> with its audit row (every redirect
/// change is audited, auth.md). It stamps the bookkeeping and decides nothing about redirects itself.
/// </summary>
/// <remarks>
/// Every staff write, and every row a slug change touches, stages <c>site.revalidate-requested</c> before the one
/// save, so the site's cached list of redirects stops being served instead of expiring on its own (ADR 0035's
/// consequence, ADR 0036). The legacy seed does not: it runs in MigrationService, before any host has cached
/// anything.
/// </remarks>
internal sealed class RedirectsDataLayer(
    IRedirectsRepository repository,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IRedirectsDataLayer
{
    /// <summary>The actor recorded on redirects a slug change writes: a system action, not a person.</summary>
    public const string SlugChangeActor = "system:slug-change";

    public Task<Redirect?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindAsync(id, cancellationToken);

    public Task<Redirect?> FindByFromAsync(string fromPath, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.FindByFromAsync(fromPath, exceptId, cancellationToken);

    public Task<Redirect?> FindTargetingAsync(string targetPath, Guid? exceptId, CancellationToken cancellationToken) =>
        repository.FindTargetingAsync(targetPath, exceptId, cancellationToken);

    public Task<List<Redirect>> ListForSlugChangeAsync(string fromPath, string toPath, CancellationToken cancellationToken) =>
        repository.ListForSlugChangeAsync(fromPath, toPath, cancellationToken);

    public Task<List<RedirectServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, RedirectSource? source, string? search, CancellationToken cancellationToken) =>
        repository.ListAsync(sort, after, take, source, search, cancellationToken);

    public Task<List<SiteRedirectServiceModel>> ListForSiteAsync(CancellationToken cancellationToken) =>
        repository.ListForSiteAsync(cancellationToken);

    public async Task<RedirectWriteOutcome> CreateAsync(Redirect redirect, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirect);

        var now = clock.UtcNow;
        redirect.CreatedAt = now;
        Stamp(redirect, actorUserId, now);
        repository.Add(redirect);
        StageRevalidation(redirect);

        audit.Record(new AuditEntry(
            actorUserId, RedirectAudit.Created, RedirectAudit.ResourceType, redirect.Id.ToString(), correlation.Current,
            $"{redirect.FromPath} → {redirect.ToPath} ({redirect.StatusCode}, {redirect.Source}).",
            AfterReference: Reference(redirect)));

        return await SaveAsync(redirect, cancellationToken);
    }

    public async Task<RedirectWriteOutcome> UpdateAsync(
        Redirect redirect, string actorUserId, string before, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirect);

        Stamp(redirect, actorUserId, clock.UtcNow);
        StageRevalidation(redirect);
        audit.Record(new AuditEntry(
            actorUserId, RedirectAudit.Updated, RedirectAudit.ResourceType, redirect.Id.ToString(), correlation.Current,
            $"Now {redirect.FromPath} → {redirect.ToPath} ({redirect.StatusCode}).",
            BeforeReference: before, AfterReference: Reference(redirect)));

        return await SaveAsync(redirect, cancellationToken);
    }

    public async Task<RedirectWriteOutcome> DeleteAsync(Redirect redirect, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirect);

        repository.Remove(redirect);
        StageRevalidation(redirect);
        audit.Record(new AuditEntry(
            actorUserId, RedirectAudit.Deleted, RedirectAudit.ResourceType, redirect.Id.ToString(), correlation.Current,
            $"Deleted {redirect.FromPath} → {redirect.ToPath}.", BeforeReference: Reference(redirect)));

        return await SaveAsync(subject: null, cancellationToken);
    }

    public async Task<RedirectWriteOutcome> ApplySlugChangeAsync(
        RedirectSlugChangePlan plan, string summary, string resourceId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var now = clock.UtcNow;
        foreach (var redirect in plan.Remove)
        {
            repository.Remove(redirect);
        }

        foreach (var redirect in plan.Changed)
        {
            Stamp(redirect, SlugChangeActor, now);
        }

        foreach (var redirect in plan.Add)
        {
            redirect.CreatedAt = now;
            Stamp(redirect, SlugChangeActor, now);
            repository.Add(redirect);
        }

        // One message per row the rename touched: each names its own redirect, and together they clear every
        // address whose answer has changed (ADR 0036).
        foreach (var redirect in plan.Remove.Concat(plan.Changed).Concat(plan.Add))
        {
            StageRevalidation(redirect);
        }

        // A system action: no user to name, so no actor (AuditLog.ActorUserId).
        audit.Record(new AuditEntry(
            null, RedirectAudit.SlugChanged, RedirectAudit.ResourceType, resourceId, correlation.Current, summary));

        // A clash here is a slug change racing a staff write: a fault the Worker retries, not a refusal.
        return await SaveAsync(subject: null, cancellationToken);
    }

    public Task<HashSet<string>> GetFromPathsAsync(CancellationToken cancellationToken) =>
        repository.GetFromPathsAsync(cancellationToken);

    public Task<bool> HasBeenSeededAsync(CancellationToken cancellationToken) =>
        repository.HasBeenSeededAsync(cancellationToken);

    public async Task<int> AddSeedAsync(IReadOnlyList<Redirect> redirects, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirects);

        var now = clock.UtcNow;
        foreach (var redirect in redirects)
        {
            redirect.CreatedAt = now;
            Stamp(redirect, actor, now);
            repository.Add(redirect);
        }

        audit.Record(new AuditEntry(
            null, RedirectAudit.Seeded, RedirectAudit.ResourceType, "legacy-seed", correlation.Current,
            $"Seeded {redirects.Count} legacy redirects: {string.Join(", ", redirects.Select(redirect => redirect.FromPath))}."));

        await repository.SaveChangesAsync(cancellationToken);
        return redirects.Count;
    }

    /// <summary>
    /// Stages <c>site.revalidate-requested</c> for one redirect: the type tag clears the site's whole list, and the
    /// path tags clear anything cached for the addresses at either end of the hop (ADR 0036).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both ends go through <see cref="TagPath"/>, because neither is guaranteed to be a cache tag: a destination
    /// may be an absolute <c>https://</c> address, and either address may carry a query
    /// (<c>RedirectPaths.NormalizeSitePath</c> keeps one, so <c>/?p=123</c> is a legal old address). An address
    /// that cannot be a tag is left out rather than carried: the type tag still clears the site's list, whereas a
    /// payload <c>TryParse</c> rejects would poison the message and clear nothing at all.
    /// </para>
    /// </remarks>
    private void StageRevalidation(Redirect redirect)
    {
        List<string> paths = [];
        foreach (var address in (string[])[redirect.FromPath, redirect.ToPath])
        {
            if (TagPath(address) is { } path && !paths.Contains(path, StringComparer.Ordinal))
            {
                paths.Add(path);
            }
        }

        outbox.Enqueue(
            SiteRevalidateRequested.MessageType,
            new SiteRevalidateRequested(RedirectAudit.ResourceType, redirect.Id, paths).Serialize(),
            correlation.Current);
    }

    /// <summary>
    /// The site path a cache tag can be made from: rooted, and without the query or fragment a tag would never
    /// match anyway. <c>null</c> for anything else — another site's address, or a path with a <c>':'</c> in it,
    /// which <c>CacheTags.SitePath</c> refuses.
    /// </summary>
    private static string? TagPath(string address)
    {
        if (address is not ['/', ..])
        {
            return null;
        }

        var path = address.AsSpan();
        var end = path.IndexOfAny('?', '#');
        if (end >= 0)
        {
            path = path[..end];
        }

        return path.Length > 0 && !path.Contains(':') ? path.ToString() : null;
    }

    private static void Stamp(Redirect redirect, string actor, DateTimeOffset now)
    {
        redirect.UpdatedAt = now;
        redirect.UpdatedBy = actor;
        redirect.RowVersion = Guid.NewGuid();
    }

    /// <summary>The audit reference for a redirect, bounded to the column: the two addresses, truncated if need be.</summary>
    private static string Reference(Redirect redirect)
    {
        var reference = $"{redirect.FromPath} → {redirect.ToPath}";
        return reference.Length <= AuditPolicy.ReferenceMaxLength ? reference : reference[..(AuditPolicy.ReferenceMaxLength - 1)] + "…";
    }

    private async Task<RedirectWriteOutcome> SaveAsync(Redirect? subject, CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return RedirectWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return RedirectWriteOutcome.Conflict;
        }
        catch (DbUpdateException) when (subject is not null)
        {
            // Most likely the unique old-address index: another redirect took it after Business checked. Asked again
            // rather than assumed, so a genuine fault is not reported as a taken address.
            repository.DiscardChanges();
            if (await repository.FindByFromAsync(subject.FromPath, subject.Id, cancellationToken) is not null)
            {
                return RedirectWriteOutcome.FromTaken;
            }

            throw;
        }
    }
}
