using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Forms.Data.Configurations;

internal sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ContactMessages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Name).IsRequired().HasMaxLength(FormsPolicy.NameMaxLength);
        builder.Property(message => message.Email).IsRequired().HasMaxLength(FormsPolicy.EmailMaxLength);
        builder.Property(message => message.Message).IsRequired().HasMaxLength(FormsPolicy.MessageMaxLength);
        builder.Property(message => message.UpdatedBy).HasMaxLength(450);
        builder.Property(message => message.RowVersion).IsConcurrencyToken();

        builder.HasIndex(message => new { message.Handled, message.ReceivedAt, message.Id })
            .HasDatabaseName("IX_ContactMessages_Handled_ReceivedAt_Id");
        builder.HasIndex(message => new { message.ReceivedAt, message.Id })
            .HasDatabaseName("IX_ContactMessages_ReceivedAt_Id");
    }
}
