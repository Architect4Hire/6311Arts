using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Managers.Caching;

/// <summary>
/// Applies the tag invalidations other processes publish to this process's HybridCache (ADR 0049): its in-memory
/// copies go, and so does the tag time it would otherwise keep from its first read of L2.
/// </summary>
/// <remarks>
/// Subscribes while the host starts, and a host that cannot subscribe does not start: without it, every published
/// change would wait out its entry's lifetime here. Kestrel may already be listening by then, which is harmless: a
/// process that has just started holds no copies, and reads every tag's time from L2 afresh.
/// </remarks>
internal sealed partial class CacheInvalidationListener(
    ICacheInvalidationBus bus, HybridCache cache, ILogger<CacheInvalidationListener> logger) : IHostedService
{
    private IAsyncDisposable? subscription;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            subscription = await bus.SubscribeAsync(ApplyAsync, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type only, as everywhere a Redis failure is reported: its message names the host and port.
            throw new InvalidOperationException(
                $"Could not subscribe to the cache invalidation channel ({ex.GetType().Name}).");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (subscription is not null)
        {
            await subscription.DisposeAsync();
            subscription = null;
        }
    }

    // Straight to HybridCache, not IAppCache: applying a tag here must not publish it again.
    private async ValueTask ApplyAsync(string tag, CancellationToken cancellationToken)
    {
        await cache.RemoveByTagAsync(tag, cancellationToken);
        LogApplied(tag);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Applied cache tag {Tag} from another process.")]
    private partial void LogApplied(string tag);
}
