namespace Arts6311.Domain.Managers.Caching;

/// <summary>
/// How a tag invalidation made in one process reaches every other process's cache (ADR 0049).
/// </summary>
/// <remarks>
/// <para>
/// HybridCache records a tag's invalidation time in L2, but each process reads that time from L2 once, the first
/// time it meets the tag, and keeps it; only an invalidation made in the same process replaces it. So the Worker's
/// <c>site.revalidate-requested</c> handler reached Redis and never the API: the API's entries lived their full
/// <see cref="SiteCachePolicy.Lifetime"/> (measured in prompt 3.4).
/// </para>
/// <para>
/// <see cref="HybridAppCache"/> therefore publishes every tag it removes, and <see cref="CacheInvalidationListener"/>
/// applies the tags other processes published to this process's HybridCache. Delivery is at most once: a message
/// missed while a process is disconnected leaves its entries no older than their lifetime, and a process that
/// (re)starts reads every tag's time from L2 afresh.
/// </para>
/// </remarks>
public interface ICacheInvalidationBus
{
    /// <summary>Tells every other process that <paramref name="tag"/> was invalidated. Throws when it cannot.</summary>
    ValueTask PublishAsync(string tag, CancellationToken cancellationToken);

    /// <summary>
    /// Calls <paramref name="onTag"/> for each tag another process publishes, until the result is disposed. A process
    /// never receives its own messages: it has already applied them.
    /// </summary>
    ValueTask<IAsyncDisposable> SubscribeAsync(
        Func<string, CancellationToken, ValueTask> onTag, CancellationToken cancellationToken);
}
