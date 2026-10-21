using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Memberships.Data.Configurations;

internal sealed class MembershipPlanConfiguration : IEntityTypeConfiguration<MembershipPlan>
{
    public void Configure(EntityTypeBuilder<MembershipPlan> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MembershipPlans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).ValueGeneratedNever();

        builder.Property(plan => plan.Name).IsRequired().HasMaxLength(MembershipPolicy.NameMaxLength);

        // Lower-case ASCII by construction (Slugs.IsValid): varchar, and no collation question.
        builder.Property(plan => plan.Slug).IsRequired().HasColumnType($"varchar({Slugs.MaxLength})");

        // Square's own word for the cadence (ANNUAL, MONTHLY, …): upper-case ASCII, so varchar.
        builder.Property(plan => plan.Cadence).HasColumnType($"varchar({MembershipPolicy.CadenceMaxLength})");

        builder.Property(plan => plan.UpdatedBy).IsRequired().HasMaxLength(MembershipPolicy.UserIdMaxLength);
        builder.Property(plan => plan.RowVersion).IsConcurrencyToken();

        // Not mapped: it is read from the projection columns, which the inbound pull owns (ADR 0005).
        builder.Ignore(plan => plan.HasSquareRecord);

        // The perks belong to the plan: they go with it, and are read and written with it.
        builder.HasMany(plan => plan.Perks)
            .WithOne()
            .HasForeignKey(perk => perk.MembershipPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(plan => plan.Slug)
            .IsUnique()
            .HasDatabaseName("IX_MembershipPlans_Slug");

        // The importer's idempotency key: unique where present, and most rows have none.
        builder.HasIndex(plan => plan.LegacyWordPressId)
            .IsUnique()
            .HasFilter("[LegacyWordPressId] IS NOT NULL")
            .HasDatabaseName("IX_MembershipPlans_LegacyWordPressId");

        // The CMS list, and the site's (Phase 3): in the order the page shows them, keyset-paged on
        // (DisplayOrder, Id), with or without a status filter.
        builder.HasIndex(plan => new { plan.DisplayOrder, plan.Id })
            .HasDatabaseName("IX_MembershipPlans_DisplayOrder_Id");

        builder.HasIndex(plan => new { plan.Status, plan.DisplayOrder, plan.Id })
            .HasDatabaseName("IX_MembershipPlans_Status_DisplayOrder_Id");

        // The Worker's scheduled-publish pass, every minute: the Scheduled rows due by now, oldest first (ADR 0036).
        builder.HasIndex(plan => new { plan.Status, plan.ScheduledFor })
            .HasDatabaseName("IX_MembershipPlans_Status_ScheduledFor");
    }
}
