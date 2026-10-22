using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Exhibitions.Data.Configurations;

internal sealed class ExhibitionConfiguration : IEntityTypeConfiguration<Exhibition>
{
    public void Configure(EntityTypeBuilder<Exhibition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Exhibitions");
        builder.HasKey(exhibition => exhibition.Id);
        builder.Property(exhibition => exhibition.Id).ValueGeneratedNever();

        builder.Property(exhibition => exhibition.Title).IsRequired().HasMaxLength(ExhibitionPolicy.TitleMaxLength);

        // Lower-case ASCII by construction (Slugs.IsValid): varchar, and no collation question.
        builder.Property(exhibition => exhibition.Slug).IsRequired().HasColumnType($"varchar({Slugs.MaxLength})");
        builder.Property(exhibition => exhibition.Summary).HasMaxLength(ExhibitionPolicy.SummaryMaxLength);
        builder.Property(exhibition => exhibition.Body).HasMaxLength(ExhibitionPolicy.BodyMaxLength);
        builder.Property(exhibition => exhibition.ReceptionNote).HasMaxLength(ExhibitionPolicy.ReceptionNoteMaxLength);
        builder.Property(exhibition => exhibition.UpdatedBy).IsRequired().HasMaxLength(ExhibitionPolicy.UserIdMaxLength);
        builder.Property(exhibition => exhibition.RowVersion).IsConcurrencyToken();
        builder.Property(exhibition => exhibition.DeletedBy).HasMaxLength(ExhibitionPolicy.UserIdMaxLength);

        // A deleted record is not there: hidden from the CMS lists, the site reads, the sitemap, previews and the
        // scheduled publisher alike, unless a query asks for deleted rows by name (ADR 0052).
        SoftDelete.Apply(builder);

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(exhibition => exhibition.HeroMediaId)
            .OnDelete(DeleteBehavior.Restrict);

        // The credits belong to the exhibition: they go with it, and are read and written with it.
        builder.HasMany(exhibition => exhibition.Artists)
            .WithOne()
            .HasForeignKey(credit => credit.ExhibitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Live rows only, so deleting a record releases its web address for the next one (ADR 0052). The
        // constraint has to say so itself: a unique index knows nothing of the query filter above.
        builder.HasIndex(exhibition => exhibition.Slug)
            .IsUnique()
            .HasFilter(SoftDelete.LiveRowsOnly)
            .HasDatabaseName("IX_Exhibitions_Slug");

        // The Deleted view: the deleted rows, in the same order as the main list.
        builder.HasIndex(exhibition => new { exhibition.DeletedAt, exhibition.Id })
            .HasDatabaseName("IX_Exhibitions_DeletedAt_Id");

        // The importer's idempotency key: unique where present, and most rows have none.
        builder.HasIndex(exhibition => exhibition.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_Exhibitions_LegacyWordPressId");

        // The CMS list: newest show first, keyset-paged on (StartDate, Id), with or without a status filter.
        builder.HasIndex(exhibition => new { exhibition.StartDate, exhibition.Id })
            .IsDescending(true, true)
            .HasDatabaseName("IX_Exhibitions_StartDate_Id");

        builder.HasIndex(exhibition => new { exhibition.Status, exhibition.StartDate, exhibition.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Exhibitions_Status_StartDate_Id");

        // The Worker's scheduled-publish pass, every minute: the Scheduled rows due by now, oldest first
        // (ADR 0036).
        builder.HasIndex(exhibition => new { exhibition.Status, exhibition.ScheduledFor })
            .HasDatabaseName("IX_Exhibitions_Status_ScheduledFor");
    }
}
