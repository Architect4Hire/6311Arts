using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Classes.Data.Configurations;

internal sealed class ClassOfferingMediaReferenceConfiguration : IEntityTypeConfiguration<ClassOfferingMediaReference>
{
    public void Configure(EntityTypeBuilder<ClassOfferingMediaReference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ClassOfferingMediaReferences");

        // Once per image per offering, however many times the body shows it.
        builder.HasKey(reference => new { reference.ClassOfferingId, reference.MediaAssetId });

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(reference => reference.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reference => reference.MediaAssetId)
            .HasDatabaseName("IX_ClassOfferingMediaReferences_MediaAssetId");
    }
}
