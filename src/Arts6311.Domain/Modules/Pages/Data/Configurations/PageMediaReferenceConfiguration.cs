using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Pages.Data.Configurations;

internal sealed class PageMediaReferenceConfiguration : IEntityTypeConfiguration<PageMediaReference>
{
    public void Configure(EntityTypeBuilder<PageMediaReference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PageMediaReferences");

        // Once per image per page, however many times the body shows it.
        builder.HasKey(reference => new { reference.PageId, reference.MediaAssetId });

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(reference => reference.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reference => reference.MediaAssetId)
            .HasDatabaseName("IX_PageMediaReferences_MediaAssetId");
    }
}
