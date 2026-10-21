using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Arts6311.Domain.Managers.Caching;

/// <summary>
/// <see cref="ICacheInvalidationBus"/> over Redis pub/sub, on the same connection HybridCache's L2 uses (Aspire's
/// <c>AddRedisDistributedCache</c> registers it). One channel; a message is this process's id, a line break and the
/// tag, so a process can skip what it sent itself (ADR 0049).
/// </summary>
internal sealed partial class RedisCacheInvalidationBus(
    IConnectionMultiplexer redis, ILogger<RedisCacheInvalidationBus> logger) : ICacheInvalidationBus
{
    internal static readonly RedisChannel Channel = RedisChannel.Literal("6311:cache:tag-invalidated");

    private readonly string instance = Guid.NewGuid().ToString("N");

    public async ValueTask PublishAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        // StackExchange.Redis's publish takes no token; its own timeout bounds the call once it is sent.
        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetSubscriber().PublishAsync(Channel, Format(instance, tag));
    }

    public async ValueTask<IAsyncDisposable> SubscribeAsync(
        Func<string, CancellationToken, ValueTask> onTag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onTag);
        cancellationToken.ThrowIfCancellationRequested();

        // A message queue rather than a callback: its handler runs one message at a time, in the order sent.
        var queue = await redis.GetSubscriber().SubscribeAsync(Channel);
        queue.OnMessage(async message =>
        {
            if (TagFrom((string?)message.Message, instance) is not { } tag)
            {
                return;
            }

            try
            {
                await onTag(tag, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The type only: a Redis failure's message names the host and port. The entry still expires.
                LogApplyFailed(ex.GetType().Name);
            }
        });

        return new Subscription(queue);
    }

    /// <summary>The message for <paramref name="tag"/>, sent by the process <paramref name="sender"/>.</summary>
    internal static string Format(string sender, string tag) => $"{sender}\n{tag}";

    /// <summary>
    /// The tag a message carries, or <c>null</c> when this process (<paramref name="self"/>) sent it or it is not one
    /// of ours: no sender, no tag, or no line break.
    /// </summary>
    internal static string? TagFrom(string? message, string self)
    {
        var separator = message?.IndexOf('\n', StringComparison.Ordinal) ?? -1;
        if (message is null || separator <= 0 || separator == message.Length - 1)
        {
            return null;
        }

        return message.AsSpan(0, separator).SequenceEqual(self) ? null : message[(separator + 1)..];
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "A cache invalidation from another process could not be applied ({ExceptionType}).")]
    private partial void LogApplyFailed(string exceptionType);

    private sealed class Subscription(ChannelMessageQueue queue) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await queue.UnsubscribeAsync();
    }
}
