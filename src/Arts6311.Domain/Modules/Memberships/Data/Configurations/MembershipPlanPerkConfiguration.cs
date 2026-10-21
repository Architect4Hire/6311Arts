using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Memberships.Data.Configurations;

internal sealed class MembershipPlanPerkConfiguration : IEntityTypeConfiguration<MembershipPlanPerk>
{
    public void Configure(EntityTypeBuilder<MembershipPlanPerk> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MembershipPlanPerks");

        // The position is the key beside the plan, so a reorder is an update of the text where it sits: one save
        // never deletes a row and adds it back, which a (plan, text) key would force (ADR 0040).
        builder.HasKey(perk => new { perk.MembershipPlanId, perk.Position });

        builder.Property(perk => perk.Text).IsRequired().HasMaxLength(MembershipPolicy.PerkMaxLength);
    }
}
