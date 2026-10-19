namespace Arts6311.Domain.Managers.Audit;

/// <summary>One safe-to-store audit entry; see <see cref="AuditLog"/> for what it must never carry.</summary>
public sealed record AuditEntry(
    string? ActorUserId,
    string Action,
    string ResourceType,
    string ResourceId,
    Guid CorrelationId,
    string Summary,
    string? BeforeReference = null,
    string? AfterReference = null);

/// <summary>
/// Used by a DataLayer that also writes domain entities on the same <c>ArtsDbContext</c>.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Stages an <see cref="AuditLog"/> row on the ambient context <strong>without saving</strong>, exactly like
    /// <c>IOutboxWriter.Enqueue</c>: the entry commits in the same <c>SaveChangesAsync</c> as the action it records,
    /// or neither does.
    /// </summary>
    void Record(AuditEntry entry);
}
