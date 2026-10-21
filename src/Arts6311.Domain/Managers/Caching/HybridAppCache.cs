using Microsoft.Extensions.Caching.Hybrid;

namespace Arts6311.Domain.Managers.Caching;

internal sealed class HybridAppCache(HybridCache cache, ICacheInvalidationBus invalidations) : IAppCache
{
    // Each process's in-memory copy lives at most this long. A tag removed in another process reaches this one through
    // the invalidation bus (ADR 0049), not through this cap: HybridCache never re-reads a tag's time from L2 once it
    // has one, so a missed message leaves an entry as old as its whole lifetime, not this.
    internal static readonly TimeSpan MaxLocalLifetime = TimeSpan.FromMinutes(1);

    public ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        IReadOnlyCollection<string> tags,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var options = new HybridCacheEntryOptions
        {
            Expiration = lifetime,
            LocalCacheExpiration = lifetime < MaxLocalLifetime ? lifetime : MaxLocalLifetime,
        };

        return cache.GetOrCreateAsync(key, factory, options, tags, cancellationToken);
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken) =>
        cache.RemoveAsync(key, cancellationToken);

    /// <summary>
    /// Invalidates the tag here and in L2, then tells every other process (ADR 0049). A failed publish throws, so the
    /// <c>site.revalidate-requested</c> message that asked is retried; invalidating a tag twice is harmless.
    /// </summary>
    public async ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(tag, cancellationToken);
        await invalidations.PublishAsync(tag, cancellationToken);
    }
}
