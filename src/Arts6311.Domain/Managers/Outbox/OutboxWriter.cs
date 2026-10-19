using System.Diagnostics;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;

namespace Arts6311.Domain.Managers.Outbox;

internal sealed class OutboxWriter(ArtsDbContext context, IClock clock) : IOutboxWriter
{
    public void Enqueue(string messageType, string payloadJson, Guid correlationId)
    {
        // Checked here, before staging: a bad value would otherwise surface only at the caller's SaveChangesAsync
        // (a SQL truncation error), or not at all (an empty correlation id that ties the message to nothing).
        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("Outbox message type is required.", nameof(messageType));
        }

        if (messageType.Length > OutboxPolicy.TypeMaxLength)
        {
            throw new ArgumentException(
                $"Outbox message type is {messageType.Length} characters; the limit is {OutboxPolicy.TypeMaxLength}.",
                nameof(messageType));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException("Outbox payload is required.", nameof(payloadJson));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Outbox correlation id is required.", nameof(correlationId));
        }

        var now = clock.UtcNow;
        context.OutboxMessages.Add(new OutboxMessage
        {
            Type = messageType,
            PayloadJson = payloadJson,
            CorrelationId = correlationId,
            TraceParent = TraceParentFor(correlationId),
            CreatedAt = now,
            AvailableAt = now,
            Attempts = 0,
            Status = OutboxMessageStatus.Pending,
        });
    }

    /// <summary>
    /// The current span's traceparent, but only when it is on the message's own correlation trace. The handler's
    /// correlation id comes from its span's trace id, so a traceparent from any other trace would hand the handler a
    /// correlation id that is not the message's. Without one, the dispatcher parents on the correlation id instead.
    /// Only a W3C id is a traceparent; a hierarchical one has nothing a W3C parent context can be built from.
    /// </summary>
    private static string? TraceParentFor(Guid correlationId) =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
        && CorrelationContext.FromTraceId(activity.TraceId) == correlationId
            ? activity.Id
            : null;
}
