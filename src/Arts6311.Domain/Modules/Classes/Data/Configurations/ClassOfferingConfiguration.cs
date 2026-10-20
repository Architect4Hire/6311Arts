using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Classes.Data.Configurations;

internal sealed class ClassOfferingConfiguration : IEntityTypeConfiguration<ClassOffering>
{
    public void Configure(EntityTypeBuilder<ClassOffering> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ClassOfferings");
        builder.HasKey(offering => offering.Id);
        builder.Property(offering => offering.Id).ValueGeneratedNever();

        builder.Property(offering => offering.Title).IsRequired().HasMaxLength(ClassPolicy.TitleMaxLength);

        // Lower-case ASCII by construction (Slugs.IsValid): varchar, and no collation question.
        builder.Property(offering => offering.Slug).IsRequired().HasColumnType($"varchar({Slugs.MaxLength})");
        builder.Property(offering => offering.Summary).HasMaxLength(ClassPolicy.SummaryMaxLength);
        builder.Property(offering => offering.Body).HasMaxLength(ClassPolicy.BodyMaxLength);
        builder.Property(offering => offering.ClassType).HasMaxLength(ClassPolicy.ClassTypeMaxLength);
        builder.Property(offering => offering.Level).HasMaxLength(ClassPolicy.LevelMaxLength);
        builder.Property(offering => offering.Instructor).HasMaxLength(ClassPolicy.InstructorMaxLength);
        builder.Property(offering => offering.Cadence).HasMaxLength(ClassPolicy.CadenceMaxLength);
        builder.Property(offering => offering.UpdatedBy).IsRequired().HasMaxLength(ClassPolicy.UserIdMaxLength);
        builder.Property(offering => offering.RowVersion).IsConcurrencyToken();

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(offering => offering.HeroMediaId)
            .OnDelete(DeleteBehavior.Restrict);

        // The sessions and the body's images belong to the offering: they go with it, and are read and written with it.
        builder.HasMany(offering => offering.Sessions)
            .WithOne()
            .HasForeignKey(session => session.ClassOfferingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(offering => offering.Images)
            .WithOne()
            .HasForeignKey(reference => reference.ClassOfferingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(offering => offering.Slug)
            .IsUnique()
            .HasDatabaseName("IX_ClassOfferings_Slug");

        // The importer's idempotency key: unique where present, and most rows have none.
        builder.HasIndex(offering => offering.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_ClassOfferings_LegacyWordPressId");

        // The next offering number on create reads the highest in use (ADR 0037).
        builder.HasIndex(offering => offering.OfferingNumber)
            .IsDescending(true)
            .HasDatabaseName("IX_ClassOfferings_OfferingNumber");

        // The CMS list: by title, keyset-paged on (Title, Id), with or without a status filter.
        builder.HasIndex(offering => new { offering.Title, offering.Id })
            .HasDatabaseName("IX_ClassOfferings_Title_Id");

        builder.HasIndex(offering => new { offering.Status, offering.Title, offering.Id })
            .HasDatabaseName("IX_ClassOfferings_Status_Title_Id");

        // The Worker's scheduled-publish pass, every minute: the Scheduled rows due by now, oldest first (ADR 0036).
        builder.HasIndex(offering => new { offering.Status, offering.ScheduledFor })
            .HasDatabaseName("IX_ClassOfferings_Status_ScheduledFor");
    }
}
