using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Artists.Data.Configurations;

internal sealed class ArtistConfiguration : IEntityTypeConfiguration<Artist>
{
    public void Configure(EntityTypeBuilder<Artist> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Artists");
        builder.HasKey(artist => artist.Id);
        builder.Property(artist => artist.Id).ValueGeneratedNever();

        builder.Property(artist => artist.Name).IsRequired().HasMaxLength(ArtistPolicy.NameMaxLength);

        // Lower-case ASCII by construction (Slugs.IsValid): varchar, and no collation question.
        builder.Property(artist => artist.Slug).IsRequired().HasColumnType($"varchar({Slugs.MaxLength})");
        builder.Property(artist => artist.Medium).HasMaxLength(ArtistPolicy.MediumMaxLength);
        builder.Property(artist => artist.Bio).HasMaxLength(ArtistPolicy.BioMaxLength);
        builder.Property(artist => artist.UpdatedBy).IsRequired().HasMaxLength(ArtistPolicy.UserIdMaxLength);
        builder.Property(artist => artist.RowVersion).IsConcurrencyToken();

        // media.md: anything that uses an image declares a Restrict foreign key to it, which is how Media's delete
        // guard finds this table without naming the module.
        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(artist => artist.PortraitMediaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(artist => artist.Slug)
            .IsUnique()
            .HasDatabaseName("IX_Artists_Slug");

        // The importer's idempotency key: unique where present, and most rows have none.
        builder.HasIndex(artist => artist.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_Artists_LegacyWordPressId");

        // The CMS list: by name, keyset-paged on (Name, Id), with or without a status filter.
        builder.HasIndex(artist => new { artist.Name, artist.Id })
            .HasDatabaseName("IX_Artists_Name_Id");

        builder.HasIndex(artist => new { artist.Status, artist.Name, artist.Id })
            .HasDatabaseName("IX_Artists_Status_Name_Id");
    }
}
