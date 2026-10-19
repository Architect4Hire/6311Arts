using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Modules.Operations.Facade;

namespace Arts6311.Domain.Modules.Operations.Managers;

/// <summary>
/// Records that a <see cref="SystemPingRequested"/> reached the Worker. The dispatcher resolves this handler in a
/// DI scope of its own per message (outbox.md), so the facade it is given is already the handler's own.
/// </summary>
/// <remarks>
/// No last-attempt terminal state: the handler calls no provider, so it can only fail when the database does, and a
/// terminal audit row could not be written then either. The Poisoned message is the visible state (outbox.md
/// "Handling", the no-provider exception).
/// </remarks>
internal sealed class SystemPingRequestedHandler(IOperationsFacade facade) : IOutboxMessageHandler
{
    public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        // Malformed on this attempt means malformed on every attempt: poison now.
        var ping = SystemPingRequested.TryParse(message.PayloadJson)
            ?? throw new OutboxPoisonException("Malformed system ping payload.");

        try
        {
            await facade.RecordPingHandledAsync(ping.PingId, message.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Name only the exception type, and don't chain it: its message must not reach LastError (outbox.md).
            throw new InvalidOperationException($"System ping failed ({ex.GetType().Name}).");
        }
    }
}
