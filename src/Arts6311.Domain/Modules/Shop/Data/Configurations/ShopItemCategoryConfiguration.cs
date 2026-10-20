using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Shop.Data.Configurations;

internal sealed class ShopItemCategoryConfiguration : IEntityTypeConfiguration<ShopItemCategory>
{
    public void Configure(EntityTypeBuilder<ShopItemCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ShopItemCategories");

        // Filed under a category once, however many times it is sent.
        builder.HasKey(category => new { category.ShopItemId, category.Category });

        // A slug from the allow-list (ShopPolicy.Categories): lower-case ASCII, so varchar.
        builder.Property(category => category.Category)
            .IsRequired()
            .HasColumnType($"varchar({ShopPolicy.CategoryMaxLength})");

        // The site's and the admin's filter: everything in one category.
        builder.HasIndex(category => category.Category)
            .HasDatabaseName("IX_ShopItemCategories_Category");
    }
}
