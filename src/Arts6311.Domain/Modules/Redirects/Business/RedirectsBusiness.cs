using System.Globalization;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Redirects.Data;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Redirects.Business;

/// <summary>
/// The redirect rules: one redirect per old address, every chain one hop, no loops (refused at write time), and
/// slug changes applied so they stay that way (ADR 0035).
/// </summary>
internal sealed class RedirectsBusiness(IRedirectsDataLayer dataLayer, IClock clock) : IRedirectsBusiness
{
    private const string What = "this redirect";

    public async Task<OperationResult<CursorPage<RedirectServiceModel>>> ListAsync(
        RedirectListViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim().ToLowerInvariant();
        var scope = string.Create(CultureInfo.InvariantCulture, $"redirects|source={request.Source}|search={search}");

        string? after = null;
        if (request.Cursor is not null)
        {
            var decoded = PageCursor.Decode(request.Cursor, scope);
            if (!decoded.Succeeded)
            {
                return OperationResult.Failure<CursorPage<RedirectServiceModel>>(decoded.Error!);
            }

            after = decoded.Value!.SortValue;
        }

        var limit = new PageRequest(request.Cursor, request.Limit).EffectiveLimit;
        var rows = await dataLayer.ListAsync(after, limit + 1, request.Source, search, cancellationToken);

        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            var last = rows[^1];
            next = new PageCursor(last.FromPath, last.Id.ToString("N")).Encode(scope);
        }

        return OperationResult.Success(new CursorPage<RedirectServiceModel>(rows, next));
    }

    public async Task<IReadOnlyList<SiteRedirectServiceModel>> ListForSiteAsync(CancellationToken cancellationToken) =>
        await dataLayer.ListForSiteAsync(cancellationToken);

    public async Task<OperationResult<RedirectServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var redirect = await dataLayer.FindAsync(id, cancellationToken);
        return redirect is null ? NotFound() : OperationResult.Success(ToServiceModel(redirect));
    }

    public async Task<OperationResult<RedirectServiceModel>> CreateAsync(
        RedirectCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);

        var from = RedirectPaths.NormalizeFrom(create.FromPath).Path!;
        var to = RedirectPaths.NormalizeTo(create.ToPath).Path!;

        if (await LinkErrorAsync(from, to, exceptId: null, cancellationToken) is { } linkError)
        {
            return OperationResult.Failure<RedirectServiceModel>(linkError);
        }

        var redirect = new Redirect
        {
            Id = Guid.CreateVersion7(clock.UtcNow),
            FromPath = from,
            ToPath = to,
            TargetPath = RedirectPaths.TargetOf(to),
            StatusCode = create.StatusCode ?? RedirectPolicy.DefaultStatusCode,
            Source = RedirectSource.Manual,
            EffectiveAt = clock.UtcNow,
        };

        return await dataLayer.CreateAsync(redirect, actorUserId, cancellationToken) switch
        {
            RedirectWriteOutcome.Saved => OperationResult.Success(ToServiceModel(redirect)),
            RedirectWriteOutcome.FromTaken => OperationResult.Failure<RedirectServiceModel>(FromTaken(from)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult<RedirectServiceModel>> UpdateAsync(
        Guid id, RedirectUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        var redirect = await dataLayer.FindAsync(id, cancellationToken);
        if (redirect is null)
        {
            return NotFound();
        }

        if (EntityTag.Verify(redirect.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure<RedirectServiceModel>(versionError);
        }

        var from = update.FromPath is null ? redirect.FromPath : RedirectPaths.NormalizeFrom(update.FromPath).Path!;
        var to = update.ToPath is null ? redirect.ToPath : RedirectPaths.NormalizeTo(update.ToPath).Path!;
        var statusCode = update.StatusCode ?? redirect.StatusCode;

        if (from == redirect.FromPath && to == redirect.ToPath && statusCode == redirect.StatusCode)
        {
            return OperationResult.Success(ToServiceModel(redirect));
        }

        if ((from != redirect.FromPath || to != redirect.ToPath)
            && await LinkErrorAsync(from, to, redirect.Id, cancellationToken) is { } linkError)
        {
            return OperationResult.Failure<RedirectServiceModel>(linkError);
        }

        var before = $"{redirect.FromPath} → {redirect.ToPath} ({redirect.StatusCode})";
        redirect.FromPath = from;
        redirect.ToPath = to;
        redirect.TargetPath = RedirectPaths.TargetOf(to);
        redirect.StatusCode = statusCode;

        // Staff's word on it now: a later slug change about either address still applies, an earlier one doesn't.
        redirect.EffectiveAt = clock.UtcNow;

        return await dataLayer.UpdateAsync(redirect, actorUserId, before.Length <= 500 ? before : before[..499] + "…", cancellationToken) switch
        {
            RedirectWriteOutcome.Saved => OperationResult.Success(ToServiceModel(redirect)),
            RedirectWriteOutcome.FromTaken => OperationResult.Failure<RedirectServiceModel>(FromTaken(from)),
            _ => Conflict(),
        };
    }

    public async Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        var redirect = await dataLayer.FindAsync(id, cancellationToken);
        if (redirect is null)
        {
            return OperationResult.Failure(OperationError.Of(ErrorCodes.NotFound, "There is no such redirect."));
        }

        if (EntityTag.Verify(redirect.RowVersion, ifMatch, What) is { } versionError)
        {
            return OperationResult.Failure(versionError);
        }

        return await dataLayer.DeleteAsync(redirect, actorUserId, cancellationToken) == RedirectWriteOutcome.Saved
            ? OperationResult.Success()
            : OperationResult.Failure(EntityTag.Conflict(What));
    }

    public async Task<OperationResult> ApplySlugChangeAsync(RedirectSlugChangeViewModel change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var from = RedirectPaths.NormalizeFrom(change.FromPath).Path!;
        var newAddress = RedirectPaths.TargetOf(RedirectPaths.NormalizeTo(change.ToPath).Path!)!;
        if (from == newAddress)
        {
            return OperationResult.Success();
        }

        var at = change.OccurredAt.ToUniversalTime();
        var rows = await dataLayer.ListForSlugChangeAsync(from, newAddress, cancellationToken);
        var remove = new List<Redirect>();
        var changed = new List<Redirect>();
        var add = new List<Redirect>();
        var notes = new List<string>();

        // 1. A redirect leaving the new address. Older than the rename: the address is live content again, so it
        //    goes. Newer: this rename is old news, so the old address follows that redirect instead — still one hop.
        var destination = newAddress;
        if (rows.FirstOrDefault(row => row.FromPath == newAddress) is { } blocker)
        {
            if (blocker.EffectiveAt <= at)
            {
                remove.Add(blocker);
                notes.Add($"retired {blocker.FromPath} → {blocker.ToPath}");
            }
            else
            {
                destination = blocker.ToPath;
            }
        }

        var destinationTarget = RedirectPaths.TargetOf(destination);

        // 2. Redirects arriving at the old address arrive at the new one, so none becomes a two-hop chain. One that
        //    would now lead to itself goes. Only those older than the rename: a newer one is staff's later word.
        foreach (var arriving in rows.Where(row => row.TargetPath == from && row.FromPath != from && !remove.Contains(row)))
        {
            if (arriving.EffectiveAt > at)
            {
                continue;
            }

            if (arriving.FromPath == destinationTarget)
            {
                remove.Add(arriving);
                notes.Add($"retired {arriving.FromPath} → {arriving.ToPath}");
                continue;
            }

            arriving.ToPath = destinationTarget is null ? destination : RedirectPaths.Retarget(arriving.ToPath, destination);
            arriving.TargetPath = RedirectPaths.TargetOf(arriving.ToPath);
            arriving.EffectiveAt = at;
            changed.Add(arriving);
            notes.Add($"repointed {arriving.FromPath} → {arriving.ToPath}");
        }

        // 3. The old address leads to the new one — unless the new one leads straight back (it is live again), or a
        //    newer word on the old address already exists.
        var existing = rows.FirstOrDefault(row => row.FromPath == from);
        if (destinationTarget == from)
        {
            if (existing is not null && existing.EffectiveAt <= at)
            {
                remove.Add(existing);
                notes.Add($"retired {existing.FromPath} → {existing.ToPath}");
            }
        }
        else if (existing is null)
        {
            add.Add(new Redirect
            {
                Id = Guid.CreateVersion7(clock.UtcNow),
                FromPath = from,
                ToPath = destination,
                TargetPath = destinationTarget,
                StatusCode = RedirectPolicy.DefaultStatusCode,
                Source = RedirectSource.SlugChange,
                EffectiveAt = at,
            });
            notes.Add($"added {from} → {destination}");
        }
        else if (existing.EffectiveAt <= at
            && (existing.ToPath != destination || existing.StatusCode != RedirectPolicy.DefaultStatusCode || existing.Source != RedirectSource.SlugChange))
        {
            existing.ToPath = destination;
            existing.TargetPath = destinationTarget;
            existing.StatusCode = RedirectPolicy.DefaultStatusCode;
            existing.Source = RedirectSource.SlugChange;
            existing.EffectiveAt = at;
            changed.Add(existing);
            notes.Add($"repointed {from} → {destination}");
        }

        var plan = new RedirectSlugChangePlan(remove, changed, add);
        if (plan.IsEmpty)
        {
            // Already applied, or nothing to do: a redelivery changes nothing (outbox.md, at-least-once).
            return OperationResult.Success();
        }

        var summary = $"The {change.ContentType} {change.ContentId:N} moved from {from} to {newAddress}: {string.Join("; ", notes)}.";
        return await dataLayer.ApplySlugChangeAsync(
            plan, summary.Length <= 500 ? summary : summary[..499] + "…", change.ContentId.ToString(), cancellationToken) switch
        {
            RedirectWriteOutcome.Saved => OperationResult.Success(),
            _ => OperationResult.Failure(EntityTag.Conflict(What)),
        };
    }

    public async Task<int> SeedAsync(
        IReadOnlyList<(string FromPath, string ToPath)> seeds, string actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        // Once per database: a redirect staff deleted stays deleted, not resurrected by the next deploy.
        if (await dataLayer.HasBeenSeededAsync(cancellationToken))
        {
            return 0;
        }

        var now = clock.UtcNow;
        var taken = await dataLayer.GetFromPathsAsync(cancellationToken);
        var redirects = new List<Redirect>();
        foreach (var (fromPath, toPath) in seeds)
        {
            var from = RedirectPaths.NormalizeFrom(fromPath).Path!;
            var to = RedirectPaths.NormalizeTo(toPath).Path!;

            // The seed is held to the same one-hop rules as staff (taken, loop, chain), or it is skipped.
            if (!taken.Add(from) || await LinkErrorAsync(from, to, exceptId: null, cancellationToken) is not null)
            {
                continue;
            }

            redirects.Add(new Redirect
            {
                Id = Guid.CreateVersion7(now),
                FromPath = from,
                ToPath = to,
                TargetPath = RedirectPaths.TargetOf(to),
                StatusCode = RedirectPolicy.DefaultStatusCode,
                Source = RedirectSource.Imported,
                EffectiveAt = now,
            });
        }

        return redirects.Count == 0 ? 0 : await dataLayer.AddSeedAsync(redirects, actor, cancellationToken);
    }

    /// <summary>
    /// Why <paramref name="from"/> → <paramref name="to"/> can't be written, or <c>null</c>: it leads to itself, its old
    /// address is taken, its destination already redirects onward (a chain, or a loop when onward is back here), or
    /// another redirect already arrives at its old address (a chain).
    /// </summary>
    private async Task<OperationError?> LinkErrorAsync(string from, string to, Guid? exceptId, CancellationToken cancellationToken)
    {
        var target = RedirectPaths.TargetOf(to);
        if (target == from)
        {
            return OperationError.Validation("A redirect can't lead to itself.", [("toPath", "Choose a different destination.")]);
        }

        if (await dataLayer.FindByFromAsync(from, exceptId, cancellationToken) is not null)
        {
            return FromTaken(from);
        }

        if (target is not null && await dataLayer.FindByFromAsync(target, exceptId, cancellationToken) is { } onward)
        {
            return onward.TargetPath == from
                ? Chain("toPath", $"This would make a loop: {target} already redirects back to {from}.")
                : Chain("toPath", $"{target} already redirects to {onward.ToPath}. Send visitors straight there instead.");
        }

        if (await dataLayer.FindTargetingAsync(from, exceptId, cancellationToken) is { } incoming)
        {
            return Chain(
                "fromPath",
                $"{incoming.FromPath} already redirects to {from}, so this would make a chain. Point {incoming.FromPath} at the new destination instead.");
        }

        return null;
    }

    private static RedirectServiceModel ToServiceModel(Redirect redirect) => new(
        redirect.Id,
        redirect.FromPath,
        redirect.ToPath,
        redirect.StatusCode,
        redirect.Source,
        redirect.LegacyWordPressId,
        redirect.CreatedAt,
        redirect.UpdatedAt,
        redirect.UpdatedBy,
        EntityTag.Format(redirect.RowVersion));

    private static OperationResult<RedirectServiceModel> NotFound() =>
        OperationResult.Failure<RedirectServiceModel>(OperationError.Of(ErrorCodes.NotFound, "There is no such redirect."));

    private static OperationResult<RedirectServiceModel> Conflict() =>
        OperationResult.Failure<RedirectServiceModel>(EntityTag.Conflict(What));

    private static OperationError FromTaken(string from) => new(
        RedirectErrorCodes.FromTaken,
        "Another redirect already leaves from this address.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { ["fromPath"] = [$"{from} already redirects. Edit that redirect instead."] });

    private static OperationError Chain(string field, string message) => new(
        RedirectErrorCodes.LoopOrChain,
        "Redirects go straight to their destination: no loops, and no chains of more than one hop.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
