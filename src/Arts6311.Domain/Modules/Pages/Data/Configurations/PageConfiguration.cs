using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Pages.Data.Configurations;

internal sealed class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Pages");
        builder.HasKey(page => page.Id);
        builder.Property(page => page.Id).ValueGeneratedNever();

        builder.Property(page => page.Title).IsRequired().HasMaxLength(PagePolicy.TitleMaxLength);

        // Lower-case ASCII by construction (Slugs.IsValid): varchar, and no collation question.
        builder.Property(page => page.Slug).IsRequired().HasColumnType($"varchar({Slugs.MaxLength})");
        builder.Property(page => page.Body).HasMaxLength(PagePolicy.BodyMaxLength);
        builder.Property(page => page.UpdatedBy).IsRequired().HasMaxLength(PagePolicy.UserIdMaxLength);
        builder.Property(page => page.RowVersion).IsConcurrencyToken();

        // The body's images belong to the page: they go with it, and are read and written with it.
        builder.HasMany(page => page.Images)
            .WithOne()
            .HasForeignKey(reference => reference.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(page => page.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Pages_Slug");

        // The importer's idempotency key: unique where present, and most rows have none.
        builder.HasIndex(page => page.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_Pages_LegacyWordPressId");

        // The CMS list: by title, keyset-paged on (Title, Id), with or without a status filter.
        builder.HasIndex(page => new { page.Title, page.Id })
            .HasDatabaseName("IX_Pages_Title_Id");

        builder.HasIndex(page => new { page.Status, page.Title, page.Id })
            .HasDatabaseName("IX_Pages_Status_Title_Id");

        // The Worker's scheduled-publish pass, every minute: the Scheduled rows due by now, oldest first
        // (ADR 0036).
        builder.HasIndex(page => new { page.Status, page.ScheduledFor })
            .HasDatabaseName("IX_Pages_Status_ScheduledFor");
    }
}
