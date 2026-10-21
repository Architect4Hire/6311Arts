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
/// (Redis). Another host reads that time from L2 only the first time it meets the tag, and keeps it, so this
/// Worker's invalidation alone would never reach the API. <see cref="HybridAppCache"/> also publishes each tag on the
/// cache invalidation bus, and every other host applies it to its own HybridCache: a published change shows on the
/// site within the Worker's poll and a render. A missed message is bounded by the entry's lifetime (ADR 0049).
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
