namespace Arts6311.Domain.Managers.Caching;

/// <summary>
/// The application cache: HybridCache (in-memory L1, Redis L2 once a host wires it) behind a narrow, tagged
/// surface. Facades read and write it (backend.md); keys come from <see cref="CacheKeys"/> and tags from
/// <see cref="CacheTags"/>.
/// </summary>
/// <remarks>
/// Values are serialized, so only data is cached: ServiceModels, never EF entities. Invalidation is by tag, driven
/// by <c>site.revalidate-requested</c> outbox handlers, never inline after a save (outbox.md).
/// </remarks>
public interface IAppCache
{
    /// <summary>Returns the cached value for <paramref name="key"/>, or runs <paramref name="factory"/> once and caches it.</summary>
    ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        IReadOnlyCollection<string> tags,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);

    /// <summary>Invalidates every entry carrying <paramref name="tag"/>.</summary>
    ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken);
}
