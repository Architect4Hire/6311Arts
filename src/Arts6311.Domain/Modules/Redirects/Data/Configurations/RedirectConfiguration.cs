using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Redirects.Data.Configurations;

internal sealed class RedirectConfiguration : IEntityTypeConfiguration<Redirect>
{
    public void Configure(EntityTypeBuilder<Redirect> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Redirects");
        builder.HasKey(redirect => redirect.Id);
        builder.Property(redirect => redirect.Id).ValueGeneratedNever();

        // Normal form is lower case already, so the database's collation has no say in what counts as one address.
        builder.Property(redirect => redirect.FromPath).IsRequired().HasMaxLength(RedirectPolicy.FromMaxLength);
        builder.Property(redirect => redirect.ToPath).IsRequired().HasMaxLength(RedirectPolicy.ToMaxLength);
        builder.Property(redirect => redirect.TargetPath).HasMaxLength(RedirectPolicy.ToMaxLength);
        builder.Property(redirect => redirect.UpdatedBy).IsRequired().HasMaxLength(RedirectPolicy.UserIdMaxLength);
        builder.Property(redirect => redirect.RowVersion).IsConcurrencyToken();

        // One redirect per old address, and the CMS list's keyset (FromPath is unique, so it orders on its own).
        builder.HasIndex(redirect => redirect.FromPath)
            .IsUnique()
            .HasDatabaseName("IX_Redirects_FromPath");

        // "Does anything already arrive here?" — the chain check, and the slug change's collapse.
        builder.HasIndex(redirect => redirect.TargetPath)
            .HasDatabaseName("IX_Redirects_TargetPath");

        builder.HasIndex(redirect => redirect.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_Redirects_LegacyWordPressId");
    }
}
