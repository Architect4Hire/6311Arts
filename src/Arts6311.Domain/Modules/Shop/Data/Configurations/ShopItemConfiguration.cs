using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Shop.Data.Configurations;

internal sealed class ShopItemConfiguration : IEntityTypeConfiguration<ShopItem>
{
    public void Configure(EntityTypeBuilder<ShopItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ShopItems");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.Title).IsRequired().HasMaxLength(ShopPolicy.TitleMaxLength);

        // Lower-case ASCII by construction (Slugs.IsValid): varchar, and no collation question.
        builder.Property(item => item.Slug).IsRequired().HasColumnType($"varchar({Slugs.MaxLength})");
        builder.Property(item => item.Description).HasMaxLength(ShopPolicy.DescriptionMaxLength);

        // Upper-case ASCII by construction (SkuPolicy.IsValid): varchar, as in the registry that enforces it.
        builder.Property(item => item.Sku).IsRequired().HasColumnType($"varchar({SkuPolicy.MaxLength})");

        builder.Property(item => item.UpdatedBy).IsRequired().HasMaxLength(ShopPolicy.UserIdMaxLength);
        builder.Property(item => item.RowVersion).IsConcurrencyToken();

        // Not mapped: it is read from the projection columns, which the inbound pull owns (ADR 0037).
        builder.Ignore(item => item.HasSquareRecord);

        // ADR 0033: the one place a foreign key crosses into Artists. Restrict, so an artist with work in the shop
        // cannot vanish from under it; archive is how an artist is retired, and the work stays theirs.
        builder.HasOne<Artist>()
            .WithMany()
            .HasForeignKey(item => item.ArtistId)
            .OnDelete(DeleteBehavior.Restrict);

        // The categories, the gallery and the description's images belong to the item: they go with it, and are
        // read and written with it.
        builder.HasMany(item => item.Categories)
            .WithOne()
            .HasForeignKey(category => category.ShopItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.Images)
            .WithOne()
            .HasForeignKey(image => image.ShopItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(item => item.DescriptionImages)
            .WithOne()
            .HasForeignKey(reference => reference.ShopItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(item => item.Slug)
            .IsUnique()
            .HasDatabaseName("IX_ShopItems_Slug");

        // The importer's idempotency key: unique where present, and most rows have none.
        builder.HasIndex(item => item.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_ShopItems_LegacyWordPressId");

        // The SKU's uniqueness across the CMS is the shared CatalogSkus registry's, not an index here (ADR 0037):
        // one index cannot span this table and ClassSessions. This one is for the lookup by SKU that the 5.x
        // inbound pull and the admin's list do.
        builder.HasIndex(item => item.Sku)
            .HasDatabaseName("IX_ShopItems_Sku");

        // The next item number on create reads the highest in use (ADR 0037).
        builder.HasIndex(item => item.ItemNumber)
            .IsDescending(true)
            .HasDatabaseName("IX_ShopItems_ItemNumber");

        // The CMS list: by title, keyset-paged on (Title, Id), with or without a status filter.
        builder.HasIndex(item => new { item.Title, item.Id })
            .HasDatabaseName("IX_ShopItems_Title_Id");

        builder.HasIndex(item => new { item.Status, item.Title, item.Id })
            .HasDatabaseName("IX_ShopItems_Status_Title_Id");

        // Phase 3's "work by this artist".
        builder.HasIndex(item => item.ArtistId)
            .HasDatabaseName("IX_ShopItems_ArtistId");

        // The Worker's scheduled-publish pass, every minute: the Scheduled rows due by now, oldest first (ADR 0036).
        builder.HasIndex(item => new { item.Status, item.ScheduledFor })
            .HasDatabaseName("IX_ShopItems_Status_ScheduledFor");
    }
}
