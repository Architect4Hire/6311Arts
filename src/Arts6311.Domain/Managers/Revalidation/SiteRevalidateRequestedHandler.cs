using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Managers.Revalidation;

/// <summary>
/// Delivers every <c>site.revalidate-requested</c> message, whichever module wrote it (ADR 0036).
/// </summary>
/// <remarks>
/// <para>
/// There is no state to reload: the event is the fact that a record changed, and the facade it calls only tells
/// the cache to stop serving what was derived from it. The next reader runs the factory and sees current data,
/// which is why a replayed or out-of-order message is harmless.
/// </para>
/// <para>
/// No terminal state is recorded on the last attempt (outbox.md's exception for a handler with no provider to
/// report on). A failed invalidation costs staleness, not correctness: every entry carries a lifetime, so the
/// cache corrects itself without this message, and the Poisoned row is its visible state.
/// </para>
/// </remarks>
internal sealed partial class SiteRevalidateRequestedHandler(
    ISiteRevalidationFacade facade, ILogger<SiteRevalidateRequestedHandler> logger) : IOutboxMessageHandler
{
    public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var request = SiteRevalidateRequested.TryParse(message.PayloadJson)
            ?? throw new OutboxPoisonException("Malformed site.revalidate-requested payload.");

        try
        {
            await facade.RevalidateAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the type: a StackExchange.Redis message names the cache host, and the dispatcher writes what it
            // is given into OutboxMessage.LastError, which any Admin can read (outbox.md).
            LogFailed(request.EntityType, request.EntityId, exception.GetType().Name, message.Attempt);
            throw new InvalidOperationException($"Revalidation failed ({exception.GetType().Name}).");
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Revalidating {EntityType} {EntityId} failed ({ExceptionType}) on attempt {Attempt}.")]
    private partial void LogFailed(string entityType, Guid entityId, string exceptionType, int attempt);
}
