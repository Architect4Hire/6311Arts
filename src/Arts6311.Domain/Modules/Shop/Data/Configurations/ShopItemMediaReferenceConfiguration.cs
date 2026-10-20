using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Shop.Data.Configurations;

internal sealed class ShopItemMediaReferenceConfiguration : IEntityTypeConfiguration<ShopItemMediaReference>
{
    public void Configure(EntityTypeBuilder<ShopItemMediaReference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ShopItemMediaReferences");

        // Once per image per item, however many times the description shows it.
        builder.HasKey(reference => new { reference.ShopItemId, reference.MediaAssetId });

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(reference => reference.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reference => reference.MediaAssetId)
            .HasDatabaseName("IX_ShopItemMediaReferences_MediaAssetId");
    }
}
