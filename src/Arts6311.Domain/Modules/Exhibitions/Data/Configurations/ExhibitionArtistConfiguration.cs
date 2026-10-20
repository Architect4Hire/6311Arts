using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Exhibitions.Data.Configurations;

internal sealed class ExhibitionArtistConfiguration : IEntityTypeConfiguration<ExhibitionArtist>
{
    public void Configure(EntityTypeBuilder<ExhibitionArtist> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ExhibitionArtists");

        // An artist is credited once per show; the key is what refuses a second credit.
        builder.HasKey(credit => new { credit.ExhibitionId, credit.ArtistId });

        // The one place a foreign key crosses into Artists (backend.md: "entity types, only where a foreign key
        // already crosses"). Restrict: an artist who is credited cannot vanish from under the credit. Artists has no
        // delete today; archive is how an artist is retired, and the credit stays.
        builder.HasOne<Artist>()
            .WithMany()
            .HasForeignKey(credit => credit.ArtistId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase 3's "shows this artist was in", and the foreign key's own lookup.
        builder.HasIndex(credit => credit.ArtistId)
            .HasDatabaseName("IX_ExhibitionArtists_ArtistId");
    }
}
