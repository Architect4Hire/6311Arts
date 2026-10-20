using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Classes.Data.Configurations;

internal sealed class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
{
    public void Configure(EntityTypeBuilder<ClassSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ClassSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        // Upper-case ASCII by construction (SkuPolicy.IsValid): varchar, as in the registry that enforces it.
        builder.Property(session => session.Sku).IsRequired().HasColumnType($"varchar({SkuPolicy.MaxLength})");

        builder.Property(session => session.DisplayTimeZone)
            .IsRequired()
            .HasColumnType($"varchar({ClassPolicy.DisplayTimeZoneMaxLength})");

        // Not mapped: it is read from the projection columns, which the inbound pull owns (ADR 0037).
        builder.Ignore(session => session.HasSquareRecord);

        // The SKU's uniqueness across the CMS is the shared CatalogSkus registry's, not an index here (ADR 0037):
        // one index cannot span this table and ShopItems. This one is for the lookup by SKU that the 5.x inbound
        // pull and the admin's grid do.
        builder.HasIndex(session => session.Sku)
            .HasDatabaseName("IX_ClassSessions_Sku");

        // The site's "what's on" and the admin's grid: an offering's sittings in start order, and every upcoming
        // session across offerings.
        builder.HasIndex(session => new { session.ClassOfferingId, session.StartsAt })
            .HasDatabaseName("IX_ClassSessions_ClassOfferingId_StartsAt");

        builder.HasIndex(session => session.StartsAt)
            .HasDatabaseName("IX_ClassSessions_StartsAt");
    }
}
