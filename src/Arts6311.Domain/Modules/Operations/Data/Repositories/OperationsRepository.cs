using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Operations.Managers;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Operations.Data.Repositories;

internal sealed class OperationsRepository(ArtsDbContext context) : IOperationsRepository
{
    /// <summary>
    /// Whether the ping already has its handled audit row. Served by IX_AuditLogs_Resource (ResourceType, ResourceId,
    /// OccurredAt).
    /// </summary>
    /// <remarks>
    /// A temporary dedupe ledger, deleted with the ping in 1.12, and not a pattern to copy: the audit table has no
    /// unique constraint, so it cannot stop two racing deliveries. A real handler keys its effect on the outbox
    /// message <c>Id</c> (outbox.md "Handling"), or uses an idempotency record with a unique scope.
    /// </remarks>
    public Task<bool> PingHandledExistsAsync(Guid pingId, CancellationToken cancellationToken)
    {
        var resourceId = pingId.ToString();
        return context.AuditLogs.AnyAsync(
            log => log.ResourceType == OperationsAudit.PingResourceType
                && log.ResourceId == resourceId
                && log.Action == OperationsAudit.PingHandled,
            cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
