using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Shop.Data.Configurations;

internal sealed class ShopItemImageConfiguration : IEntityTypeConfiguration<ShopItemImage>
{
    public void Configure(EntityTypeBuilder<ShopItemImage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ShopItemImages");

        // One photograph appears once per item; the key is what refuses a second copy of it.
        builder.HasKey(image => new { image.ShopItemId, image.MediaAssetId });

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(image => image.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(image => image.MediaAssetId)
            .HasDatabaseName("IX_ShopItemImages_MediaAssetId");
    }
}
