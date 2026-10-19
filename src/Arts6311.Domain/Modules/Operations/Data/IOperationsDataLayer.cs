namespace Arts6311.Domain.Modules.Operations.Data;

internal interface IOperationsDataLayer
{
    /// <summary>
    /// Records the <c>system.ping-requested</c> audit row and stages <c>system.ping-requested</c>, then saves once:
    /// both commit or neither does. Returns the correlation id they were written under.
    /// </summary>
    Task<Guid> RecordPingRequestedAsync(Guid pingId, CancellationToken cancellationToken);

    /// <summary>Whether this ping already has its <c>system.ping-handled</c> row.</summary>
    Task<bool> PingHandledRecordedAsync(Guid pingId, CancellationToken cancellationToken);

    /// <summary>Records <c>system.ping-handled</c> and saves.</summary>
    Task RecordPingHandledAsync(Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken);
}
