using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Instructors.Data.Configurations;

internal sealed class InstructorConfiguration : IEntityTypeConfiguration<Instructor>
{
    public void Configure(EntityTypeBuilder<Instructor> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Instructors");
        builder.HasKey(instructor => instructor.Id);
        builder.Property(instructor => instructor.Id).ValueGeneratedNever();

        builder.Property(instructor => instructor.Name).IsRequired().HasMaxLength(InstructorPolicy.NameMaxLength);

        // Lower-case ASCII by construction (Slugs.IsValid): varchar, and no collation question.
        builder.Property(instructor => instructor.Slug).IsRequired().HasColumnType($"varchar({Slugs.MaxLength})");
        builder.Property(instructor => instructor.Medium).HasMaxLength(InstructorPolicy.MediumMaxLength);
        builder.Property(instructor => instructor.Bio).HasMaxLength(InstructorPolicy.BioMaxLength);
        builder.Property(instructor => instructor.Website).HasMaxLength(InstructorPolicy.WebsiteMaxLength);

        // ASCII by construction (InstructorPolicy.IsInstagramHandle): varchar, as the slug.
        builder.Property(instructor => instructor.Instagram)
            .HasColumnType($"varchar({InstructorPolicy.InstagramMaxLength})");
        builder.Property(instructor => instructor.UpdatedBy).IsRequired().HasMaxLength(InstructorPolicy.UserIdMaxLength);
        builder.Property(instructor => instructor.RowVersion).IsConcurrencyToken();
        builder.Property(instructor => instructor.DeletedBy).HasMaxLength(InstructorPolicy.UserIdMaxLength);

        // A deleted record is not there: hidden from the CMS lists, the site reads, the sitemap, previews and the
        // scheduled publisher alike, unless a query asks for deleted rows by name (ADR 0052).
        SoftDelete.Apply(builder);

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(instructor => instructor.PortraitMediaId)
            .OnDelete(DeleteBehavior.Restrict);

        // ADR 0043: a foreign key into Artists, as a shop item's credit (ADR 0033). Restrict, so an artist an
        // instructor is linked to cannot vanish from under the link; archive is how an artist is retired.
        builder.HasOne<Artist>()
            .WithMany()
            .HasForeignKey(instructor => instructor.ArtistId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(instructor => instructor.ArtistId)
            .HasDatabaseName("IX_Instructors_ArtistId");

        // Live rows only, so deleting a record releases its web address for the next one (ADR 0052). The
        // constraint has to say so itself: a unique index knows nothing of the query filter above.
        builder.HasIndex(instructor => instructor.Slug)
            .IsUnique()
            .HasFilter(SoftDelete.LiveRowsOnly)
            .HasDatabaseName("IX_Instructors_Slug");

        // The Deleted view: the deleted rows, in the same order as the main list.
        builder.HasIndex(instructor => new { instructor.DeletedAt, instructor.Id })
            .HasDatabaseName("IX_Instructors_DeletedAt_Id");

        // The importer's idempotency key: unique where present, and most rows have none.
        builder.HasIndex(instructor => instructor.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_Instructors_LegacyWordPressId");

        // The CMS list: by name, keyset-paged on (Name, Id), with or without a status filter.
        builder.HasIndex(instructor => new { instructor.Name, instructor.Id })
            .HasDatabaseName("IX_Instructors_Name_Id");

        builder.HasIndex(instructor => new { instructor.Status, instructor.Name, instructor.Id })
            .HasDatabaseName("IX_Instructors_Status_Name_Id");

        // The Worker's scheduled-publish pass, every minute: the Scheduled rows due by now, oldest first
        // (ADR 0036).
        builder.HasIndex(instructor => new { instructor.Status, instructor.ScheduledFor })
            .HasDatabaseName("IX_Instructors_Status_ScheduledFor");
    }
}
