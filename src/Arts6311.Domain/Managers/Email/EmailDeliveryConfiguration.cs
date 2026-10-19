using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Managers.Email;

internal sealed class EmailDeliveryConfiguration : IEntityTypeConfiguration<EmailDelivery>
{
    public void Configure(EntityTypeBuilder<EmailDelivery> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("EmailDeliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).ValueGeneratedOnAdd();

        builder.Property(delivery => delivery.Kind)
            .IsRequired()
            .HasMaxLength(EmailDeliveryPolicy.KindMaxLength);

        builder.Property(delivery => delivery.SubjectId)
            .IsRequired()
            .HasMaxLength(EmailDeliveryPolicy.SubjectIdMaxLength);

        // Fixed-length hex, so varchar rather than nvarchar: half the bytes and no collation surprises.
        builder.Property(delivery => delivery.RecipientHash)
            .HasColumnType($"varchar({EmailDeliveryPolicy.RecipientHashLength})");

        builder.Property(delivery => delivery.HandledAt).IsRequired();

        // The whole point of the table: one delivery per outbox message, enforced by the database rather than by
        // the handler remembering to check. Two racing deliveries both see no row, and the second insert loses.
        builder.HasIndex(delivery => delivery.OutboxMessageId)
            .IsUnique()
            .HasDatabaseName("IX_EmailDeliveries_OutboxMessageId");

        // For "what did we send about this?" in support.
        builder.HasIndex(delivery => new { delivery.Kind, delivery.SubjectId })
            .HasDatabaseName("IX_EmailDeliveries_Kind_SubjectId");
    }
}
