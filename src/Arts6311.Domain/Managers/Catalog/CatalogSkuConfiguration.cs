using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Managers.Catalog;

internal sealed class CatalogSkuConfiguration : IEntityTypeConfiguration<CatalogSku>
{
    public void Configure(EntityTypeBuilder<CatalogSku> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CatalogSkus");

        // The SKU is the key, which is how one unique constraint covers every content type that registers one
        // (ADR 0037). Upper-case ASCII by construction (SkuPolicy.IsValid): varchar, and no collation question.
        builder.HasKey(row => row.Sku);
        builder.Property(row => row.Sku).HasColumnType($"varchar({SkuPolicy.MaxLength})");

        builder.Property(row => row.OwnerType).IsRequired().HasColumnType($"varchar({CatalogOwnerTypes.MaxLength})");

        // One SKU per record: a session or a shop item cannot hold two.
        builder.HasIndex(row => new { row.OwnerType, row.OwnerId })
            .IsUnique()
            .HasDatabaseName("IX_CatalogSkus_OwnerType_OwnerId");
    }
}
