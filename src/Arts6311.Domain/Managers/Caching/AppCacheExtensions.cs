namespace Arts6311.Domain.Managers.Caching;

public static class AppCacheExtensions
{
    /// <summary>
    /// As <see cref="IAppCache.GetOrCreateAsync{T}"/>, but an answer of "not found" (<c>null</c>) is not kept.
    /// </summary>
    /// <remarks>
    /// A site read keys on a slug the visitor chose, so keeping misses would let anyone fill the cache with addresses
    /// that do not exist; and a lookup on a unique slug is cheap. The entry the factory wrote is removed straight
    /// after, so a concurrent reader may still see the miss once, which is harmless: it is the same answer (ADR 0042).
    /// </remarks>
    public static async ValueTask<T?> GetOrCreateFoundAsync<T>(
        this IAppCache cache,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        IReadOnlyCollection<string> tags,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(cache);

        var value = await cache.GetOrCreateAsync(key, factory, tags, lifetime, cancellationToken);
        if (value is null)
        {
            await cache.RemoveAsync(key, cancellationToken);
        }

        return value;
    }
}
