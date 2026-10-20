using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
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
/// No write stages an outbox message yet. The site's cached copy of the list expires on its own until 2.8's
/// <c>site.revalidate-requested</c> clears it on a change (ADR 0035).
/// </remarks>
internal sealed class RedirectsDataLayer(
    IRedirectsRepository repository,
    IAuditWriter audit,
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
        string? afterFromPath, int take, RedirectSource? source, string? search, CancellationToken cancellationToken) =>
        repository.ListAsync(afterFromPath, take, source, search, cancellationToken);

    public Task<List<SiteRedirectServiceModel>> ListForSiteAsync(CancellationToken cancellationToken) =>
        repository.ListForSiteAsync(cancellationToken);

    public async Task<RedirectWriteOutcome> CreateAsync(Redirect redirect, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redirect);

        var now = clock.UtcNow;
        redirect.CreatedAt = now;
        Stamp(redirect, actorUserId, now);
        repository.Add(redirect);

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
