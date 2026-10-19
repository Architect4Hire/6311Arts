using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Identity.Data.Configurations;

/// <summary>
/// Refines Identity's own user mapping. It runs after <c>IdentityDbContext.OnModelCreating</c>, so the table,
/// keys and unique indexes are already in place and only the columns this module added need describing.
/// </summary>
internal sealed class ArtsUserConfiguration : IEntityTypeConfiguration<ArtsUser>
{
    public void Configure(EntityTypeBuilder<ArtsUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Bounded rather than nvarchar(max): it is shown in the CMS and the member area, and an unbounded column
        // is both a storage surprise and a place to paste something that breaks a layout.
        builder.Property(user => user.DisplayName)
            .IsRequired()
            .HasMaxLength(IdentityPolicy.DisplayNameMaxLength);

        builder.Property(user => user.CreatedAt).IsRequired();
    }
}
