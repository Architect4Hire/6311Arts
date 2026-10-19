using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;

namespace Arts6311.Domain.Managers.Audit;

internal sealed class AuditWriter(ArtsDbContext context, IClock clock) : IAuditWriter
{
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Checked here, before staging: an over-long value would otherwise surface only as a SQL truncation error
        // at the caller's SaveChangesAsync, after the rest of its work, with nothing pointing back to the audit row.
        Required(entry.Action, AuditPolicy.ActionMaxLength, nameof(entry.Action));
        Required(entry.ResourceType, AuditPolicy.ResourceTypeMaxLength, nameof(entry.ResourceType));
        Required(entry.ResourceId, AuditPolicy.ResourceIdMaxLength, nameof(entry.ResourceId));
        Required(entry.Summary, AuditPolicy.SummaryMaxLength, nameof(entry.Summary));
        Optional(entry.ActorUserId, AuditPolicy.ActorUserIdMaxLength, nameof(entry.ActorUserId));
        Optional(entry.BeforeReference, AuditPolicy.ReferenceMaxLength, nameof(entry.BeforeReference));
        Optional(entry.AfterReference, AuditPolicy.ReferenceMaxLength, nameof(entry.AfterReference));

        context.AuditLogs.Add(new AuditLog
        {
            ActorUserId = entry.ActorUserId,
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            CorrelationId = entry.CorrelationId,
            OccurredAt = clock.UtcNow,
            Summary = entry.Summary,
            BeforeReference = entry.BeforeReference,
            AfterReference = entry.AfterReference,
        });
    }

    private static void Required(string value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Audit {field} is required.", field);
        }

        Optional(value, maxLength, field);
    }

    private static void Optional(string? value, int maxLength, string field)
    {
        if (value is not null && value.Length > maxLength)
        {
            throw new ArgumentException($"Audit {field} is {value.Length} characters; the limit is {maxLength}.", field);
        }
    }
}
