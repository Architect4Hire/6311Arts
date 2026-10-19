namespace Arts6311.Domain.Managers.Outbox;

/// <param name="Id">
/// The outbox row's own id — never the correlated domain record's id. Unique per message, so it is the
/// idempotency key for any external write the handler makes (the Square idempotency key).
/// </param>
/// <param name="Type">The routing key the handler was registered under.</param>
/// <param name="PayloadJson">The event record's ids-only payload. Never a raw secret, token, or credential.</param>
/// <param name="CorrelationId">
/// Ties this delivery back to the request that enqueued it. Shared by every message that request staged, so it
/// is never an idempotency key.
/// </param>
/// <param name="Attempt">This delivery's 1-based attempt number; <see cref="OutboxPolicy.MaxAttempts"/> is the last.</param>
public sealed record OutboxMessageEnvelope(Guid Id, string Type, string PayloadJson, Guid CorrelationId, int Attempt);

/// <summary>
/// Handles every outbox message registered under one <see cref="OutboxMessageEnvelope.Type"/>. Register with
/// keyed DI in the owning module: <c>services.AddKeyedScoped&lt;IOutboxMessageHandler, MyHandler&gt;(MyEvent.MessageType)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is at-least-once, not exactly-once: a crash between a handler completing its side effect and the
/// dispatcher marking the message Completed redelivers it. Implementations must be idempotent — key external
/// writes on <see cref="OutboxMessageEnvelope.Id"/>, and skip when the target already matches (outbox.md).
/// </para>
/// <para>
/// A handler reloads current state through a facade rather than trusting the payload, and rethrows failures as
/// a generic exception naming only the original exception's type: the dispatcher stores the message it is given
/// in <see cref="OutboxMessage.LastError"/>, so a provider message, host, key or token must never reach it.
/// </para>
/// </remarks>
public interface IOutboxMessageHandler
{
    Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken);
}
