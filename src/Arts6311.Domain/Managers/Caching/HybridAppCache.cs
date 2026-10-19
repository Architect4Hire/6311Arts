using Microsoft.Extensions.Caching.Hybrid;

namespace Arts6311.Domain.Managers.Caching;

internal sealed class HybridAppCache(HybridCache cache) : IAppCache
{
    // Each replica's in-memory copy lives at most this long, so a tag invalidated on one replica reaches the
    // others within a minute even before L2 tag metadata is consulted.
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

    public ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken) =>
        cache.RemoveByTagAsync(tag, cancellationToken);
}
