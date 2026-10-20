using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Outbox.Events;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Managers.Revalidation;

/// <summary>
/// Turns one event into tag invalidations on <see cref="IAppCache"/> (ADR 0036).
/// </summary>
/// <remarks>
/// <para>
/// <strong>How far an invalidation reaches.</strong> HybridCache's tag invalidation is logical, not a delete: it
/// records "ignore anything tagged this and created before now" in the host that calls it and in the shared L2
/// (Redis), so another host's reader treats its L2 copy as a miss. What it cannot do is reach into another host's
/// in-memory L1, which keeps its copy until <c>LocalCacheExpiration</c>. <see cref="HybridAppCache"/> caps that at
/// <see cref="HybridAppCache.MaxLocalLifetime"/>, so a published change shows on the site within that long at
/// worst (ADR 0036).
/// </para>
/// <para>
/// Idempotent by nature: invalidating a tag twice records the same "ignore older entries" instant, so a
/// redelivered message has no second effect.
/// </para>
/// </remarks>
internal sealed partial class SiteRevalidationFacade(
    IAppCache cache, ILogger<SiteRevalidationFacade> logger) : ISiteRevalidationFacade
{
    public async Task RevalidateAsync(SiteRevalidateRequested request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tags = TagsFor(request);
        foreach (var tag in tags)
        {
            await cache.RemoveByTagAsync(tag, cancellationToken);
        }

        LogRevalidated(request.EntityType, request.EntityId, tags.Count);
    }

    /// <summary>
    /// The tags one event clears: the type, the entity, and each distinct path. A list page carries the type tag
    /// and an item page both the entity and the path tag, so either kind of change clears what showed the record
    /// (<see cref="CacheTags"/>).
    /// </summary>
    internal static IReadOnlyList<string> TagsFor(SiteRevalidateRequested request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tags = new List<string>(request.Paths.Count + 2)
        {
            CacheTags.Type(request.EntityType),
            CacheTags.Entity(request.EntityType, request.EntityId.ToString()),
        };

        foreach (var path in request.Paths.Distinct(StringComparer.Ordinal))
        {
            tags.Add(CacheTags.SitePath(path));
        }

        return tags;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Revalidated {EntityType} {EntityId}: {TagCount} tags.")]
    private partial void LogRevalidated(string entityType, Guid entityId, int tagCount);
}
