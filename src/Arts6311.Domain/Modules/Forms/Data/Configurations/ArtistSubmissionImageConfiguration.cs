using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Forms.Data.Configurations;

internal sealed class ArtistSubmissionImageConfiguration : IEntityTypeConfiguration<ArtistSubmissionImage>
{
    public void Configure(EntityTypeBuilder<ArtistSubmissionImage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ArtistSubmissionImages");

        // Once per picture per submission: a picture sent twice is one row.
        builder.HasKey(image => new { image.SubmissionId, image.MediaAssetId });

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(image => image.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(image => image.MediaAssetId)
            .HasDatabaseName("IX_ArtistSubmissionImages_MediaAssetId");
    }
}
