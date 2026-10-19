using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Modules.Operations.Data.Repositories;
using Arts6311.Domain.Modules.Operations.Managers;

namespace Arts6311.Domain.Modules.Operations.Data;

internal sealed class OperationsDataLayer(
    IOperationsRepository repository,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation) : IOperationsDataLayer
{
    public async Task<Guid> RecordPingRequestedAsync(Guid pingId, CancellationToken cancellationToken)
    {
        var correlationId = correlation.Current;

        // No actor until 1.7 brings authentication: the route is Development-only scaffolding until then.
        audit.Record(new AuditEntry(
            ActorUserId: null,
            OperationsAudit.PingRequested,
            OperationsAudit.PingResourceType,
            pingId.ToString(),
            correlationId,
            "System ping requested."));
        outbox.Enqueue(SystemPingRequested.MessageType, new SystemPingRequested(pingId).Serialize(), correlationId);

        // One save: the audit row and the message commit together, or neither does.
        await repository.SaveChangesAsync(cancellationToken);
        return correlationId;
    }

    public Task<bool> PingHandledRecordedAsync(Guid pingId, CancellationToken cancellationToken) =>
        repository.PingHandledExistsAsync(pingId, cancellationToken);

    public async Task RecordPingHandledAsync(Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        audit.Record(new AuditEntry(
            ActorUserId: null,
            OperationsAudit.PingHandled,
            OperationsAudit.PingResourceType,
            pingId.ToString(),
            correlation.Current,
            "System ping handled by the Worker.",
            AfterReference: $"outbox:{outboxMessageId}"));

        await repository.SaveChangesAsync(cancellationToken);
    }
}
