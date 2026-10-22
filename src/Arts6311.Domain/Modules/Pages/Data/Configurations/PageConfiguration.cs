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
        builder.Property(page => page.DeletedBy).HasMaxLength(PagePolicy.UserIdMaxLength);
        builder.Property(page => page.RowVersion).IsConcurrencyToken();

        // A deleted page is not there: hidden from the CMS lists, the site read, the sitemap, previews and the
        // scheduled publisher alike, unless a query asks for deleted rows by name (ADR 0052).
        SoftDelete.Apply(builder);

        // The body's images belong to the page: they go with it, and are read and written with it.
        builder.HasMany(page => page.Images)
            .WithOne()
            .HasForeignKey(reference => reference.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Live rows only, so deleting a page releases its web address for the next one (ADR 0052). The constraint
        // has to say so itself: a unique index knows nothing of the query filter above.
        builder.HasIndex(page => page.Slug)
            .IsUnique()
            .HasFilter(SoftDelete.LiveRowsOnly)
            .HasDatabaseName("IX_Pages_Slug");

        // The importer's idempotency key: unique where present, and most rows have none. Deliberately *not*
        // filtered by DeletedAt — the importer reads it past the query filter, so a page staff deleted stays
        // matched to its WordPress id and a re-import skips it rather than bringing it back (ADR 0052).
        builder.HasIndex(page => page.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_Pages_LegacyWordPressId");

        // The Deleted view: the deleted rows, in the same (Title, Id) order as the main list.
        builder.HasIndex(page => new { page.DeletedAt, page.Title, page.Id })
            .HasDatabaseName("IX_Pages_DeletedAt_Title_Id");

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
