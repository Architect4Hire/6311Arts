using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Classes.Data.Configurations;

internal sealed class ClassRunConfiguration : IEntityTypeConfiguration<ClassRun>
{
    public void Configure(EntityTypeBuilder<ClassRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ClassRuns");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();

        builder.Property(run => run.Label).HasMaxLength(ClassPolicy.RunLabelMaxLength);
        builder.Property(run => run.Cadence).HasMaxLength(ClassPolicy.CadenceMaxLength);

        // Upper-case ASCII by construction (SkuPolicy.IsValid): varchar, as in the registry that enforces it.
        builder.Property(run => run.Sku).IsRequired().HasColumnType($"varchar({SkuPolicy.MaxLength})");

        builder.Property(run => run.DisplayTimeZone)
            .IsRequired()
            .HasColumnType($"varchar({ClassPolicy.DisplayTimeZoneMaxLength})");

        // Not mapped: it is read from the projection columns, which the inbound pull owns (ADR 0037).
        builder.Ignore(run => run.HasSquareRecord);

        // ADR 0044: a foreign key into Instructors, as a shop item's into Artists (ADR 0033). Restrict, so an
        // instructor who teaches a run cannot vanish from under it; archive is how an instructor is retired.
        builder.HasOne<Instructor>()
            .WithMany()
            .HasForeignKey(run => run.InstructorId)
            .OnDelete(DeleteBehavior.Restrict);

        // The sittings belong to the run: they go with it, and are read and written with it.
        builder.HasMany(run => run.Sessions)
            .WithOne()
            .HasForeignKey(session => session.ClassRunId)
            .OnDelete(DeleteBehavior.Cascade);

        // The SKU's uniqueness across the CMS is the shared CatalogSkus registry's, not an index here (ADR 0037):
        // one index cannot span this table and ShopItems. This one is for the lookup by SKU that the 5.x inbound
        // pull does.
        builder.HasIndex(run => run.Sku)
            .HasDatabaseName("IX_ClassRuns_Sku");

        // The site's "classes with this instructor".
        builder.HasIndex(run => run.InstructorId)
            .HasDatabaseName("IX_ClassRuns_InstructorId");
    }
}
