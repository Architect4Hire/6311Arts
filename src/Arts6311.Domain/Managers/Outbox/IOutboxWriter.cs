namespace Arts6311.Domain.Managers.Outbox;

/// <summary>
/// Injected only into a DataLayer that also writes domain entities on the same <c>ArtsDbContext</c> — never into
/// Business, a facade or a controller (outbox.md).
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Stages an <see cref="OutboxMessage"/> row on the ambient DbContext without saving, so it commits in
    /// the same <c>SaveChangesAsync</c> as the domain write the caller is already about to save — the
    /// domain write and the event commit together, or neither does. This is the whole "transaction helper":
    /// no separate <c>BeginTransactionAsync</c> is needed because one <c>SaveChangesAsync</c> over multiple
    /// pending entities on one <c>DbContext</c> is already one transaction.
    /// </summary>
    /// <param name="messageType">The handler's routing key, kebab-case <c>area.event</c>.</param>
    /// <param name="payloadJson">The event record's <c>Serialize()</c> output: ids only.</param>
    /// <param name="correlationId">The request's <c>ICorrelationContext.Current</c>.</param>
    void Enqueue(string messageType, string payloadJson, Guid correlationId);
}
