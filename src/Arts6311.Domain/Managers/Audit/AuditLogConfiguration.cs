using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Managers.Audit;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(log => log.Id);

        builder.Property(log => log.ActorUserId).HasMaxLength(AuditPolicy.ActorUserIdMaxLength);
        builder.Property(log => log.Action).IsRequired().HasMaxLength(AuditPolicy.ActionMaxLength);
        builder.Property(log => log.ResourceType).IsRequired().HasMaxLength(AuditPolicy.ResourceTypeMaxLength);
        builder.Property(log => log.ResourceId).IsRequired().HasMaxLength(AuditPolicy.ResourceIdMaxLength);
        builder.Property(log => log.CorrelationId).IsRequired();
        builder.Property(log => log.OccurredAt).IsRequired();
        builder.Property(log => log.Summary).IsRequired().HasMaxLength(AuditPolicy.SummaryMaxLength);
        builder.Property(log => log.BeforeReference).HasMaxLength(AuditPolicy.ReferenceMaxLength);
        builder.Property(log => log.AfterReference).HasMaxLength(AuditPolicy.ReferenceMaxLength);

        // The admin's history view: newest first.
        builder.HasIndex(log => log.OccurredAt).HasDatabaseName("IX_AuditLogs_OccurredAt");

        // The history of one item.
        builder.HasIndex(log => new { log.ResourceType, log.ResourceId, log.OccurredAt })
            .HasDatabaseName("IX_AuditLogs_Resource");
    }
}
