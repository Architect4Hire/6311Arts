using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Forms.Data.Configurations;

internal sealed class NewsletterSubscriberConfiguration : IEntityTypeConfiguration<NewsletterSubscriber>
{
    public void Configure(EntityTypeBuilder<NewsletterSubscriber> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("NewsletterSubscribers");
        builder.HasKey(subscriber => subscriber.Id);
        builder.Property(subscriber => subscriber.Id).ValueGeneratedNever();
        builder.Property(subscriber => subscriber.Email).IsRequired().HasMaxLength(FormsPolicy.EmailMaxLength);
        builder.Property(subscriber => subscriber.ConsentVersion).IsRequired().HasMaxLength(32);

        // One row per person: the address is stored lower-cased, so the collation has no say.
        builder.HasIndex(subscriber => subscriber.Email)
            .IsUnique()
            .HasDatabaseName("IX_NewsletterSubscribers_Email");
        builder.HasIndex(subscriber => new { subscriber.CreatedAt, subscriber.Id })
            .HasDatabaseName("IX_NewsletterSubscribers_CreatedAt_Id");
    }
}
