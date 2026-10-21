namespace Arts6311.Domain.Modules.Memberships.Managers;

/// <summary>One development membership tier, as the seeder hands it to Business.</summary>
/// <param name="StartingPriceCents">What it bills; the figure the first Square push would create the plan with.</param>
/// <param name="DiscountPercent">What a member of this tier saves on every class.</param>
/// <param name="Featured">Whether the page highlights it.</param>
/// <param name="DisplayOrder">Where it comes among the tiers, from 1.</param>
internal sealed record MembershipPlanSeed(
    string Name,
    string Slug,
    long StartingPriceCents,
    MembershipCadence Cadence,
    int DiscountPercent,
    bool Featured,
    int DisplayOrder,
    IReadOnlyList<string> Perks);
