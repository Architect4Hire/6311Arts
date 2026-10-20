using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Media.Data.Configurations;

internal sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    /// <summary>Identity's user id column length, so <c>UploadedBy</c> can hold any user id.</summary>
    private const int UserIdMaxLength = 450;

    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MediaAssets");
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Id).ValueGeneratedNever();

        // A rendition belongs to its original. NoAction rather than Cascade: SQL Server refuses a cascading
        // self-reference ("may cause cycles"), so the DataLayer deletes the renditions itself in the same save.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(asset => asset.ParentAssetId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(asset => asset.RenditionName).HasColumnType("varchar(16)");
        builder.Property(asset => asset.FileName).HasMaxLength(MediaPolicy.FileNameMaxLength);
        builder.Property(asset => asset.MimeType).IsRequired().HasColumnType("varchar(32)");

        // Fixed-length hex and ASCII keys: varchar, half the bytes of nvarchar and no collation surprises.
        builder.Property(asset => asset.Checksum).IsRequired().HasColumnType("varchar(64)");
        builder.Property(asset => asset.BlobKey).IsRequired().HasColumnType("varchar(200)");
        builder.Property(asset => asset.ProfileHash).HasColumnType("varchar(16)");

        builder.Property(asset => asset.AltText).HasMaxLength(MediaPolicy.AltTextMaxLength);
        builder.Property(asset => asset.Credit).HasMaxLength(MediaPolicy.CreditMaxLength);
        builder.Property(asset => asset.UploadedBy).IsRequired().HasMaxLength(UserIdMaxLength);
        builder.Property(asset => asset.UpdatedBy).HasMaxLength(UserIdMaxLength);

        builder.Property(asset => asset.RowVersion).IsConcurrencyToken();

        // One row per rendition of an original: a redelivered rendition message finds it rather than adding a twin,
        // and two racing deliveries collide here instead of both inserting.
        builder.HasIndex(asset => new { asset.ParentAssetId, asset.RenditionName })
            .IsUnique()
            .HasDatabaseName("IX_MediaAssets_ParentAssetId_RenditionName");

        // The library: originals, newest first, keyset-paged on (UploadedAt, Id).
        builder.HasIndex(asset => new { asset.Kind, asset.UploadedAt, asset.Id })
            .HasDatabaseName("IX_MediaAssets_Kind_UploadedAt_Id");

        // "Is this exact file already in the library?"
        builder.HasIndex(asset => new { asset.Checksum, asset.Kind })
            .HasDatabaseName("IX_MediaAssets_Checksum_Kind");
    }
}
