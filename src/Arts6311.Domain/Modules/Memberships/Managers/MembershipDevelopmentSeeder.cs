using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Memberships.Business;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Memberships.Managers;

/// <summary>
/// The spike's three membership tiers (<c>projects/site/src/app/data/mock-shop.ts</c>, <c>MEMBERSHIPS</c>), so a
/// clean clone's CMS and, from Phase 3, its site have tiers to show. Development only.
/// </summary>
/// <remarks>
/// <para>
/// Idempotent as <see cref="IDataSeeder"/> requires: a tier whose slug is already in use is left alone, so a rerun
/// adds nothing and never undoes a staff edit.
/// </para>
/// <para>
/// The tiers are seeded Published, as the other content seeders do, because the spike's page shows all three. No
/// price or cadence <em>projection</em> is seeded: only the starting figures, which are the CMS's (ADR 0005).
/// </para>
/// </remarks>
internal sealed partial class MembershipDevelopmentSeeder(
    IMembershipsBusiness business,
    ILogger<MembershipDevelopmentSeeder> logger) : IDataSeeder
{
    /// <summary>
    /// <c>UpdatedBy</c> on a seeded plan. Not a user id, so the importer's "edited by staff since?" comparison
    /// (content.md) can tell a seeded row from a staff one. The same actor as the other content seeds.
    /// </summary>
    public const string SeedActor = "system:development-seed";

    /// <summary>The spike's tiers, in the order its page shows them, with its own slugs, prices and perks.</summary>
    internal static readonly IReadOnlyList<MembershipPlanSeed> Tiers =
    [
        new("Gray", "gray-membership", 10_000, MembershipCadence.Annual, 5, false, 1,
            ["5% off every class & workshop", "Annual membership party", "6311arts tote"]),
        new("Black", "black-membership", 30_000, MembershipCadence.Annual, 10, true, 2,
            ["10% off every class & workshop", "Annual membership party", "6311arts t-shirt"]),
        new("Blue", "blue-membership", 50_000, MembershipCadence.Annual, 15, false, 3,
            ["15% off every class & workshop", "Annual membership party", "6311arts tote", "6311arts t-shirt"]),
    ];

    public string Name => "Development membership plans";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var added = await business.SeedAsync(Tiers, SeedActor, cancellationToken);
        LogSeeded(added);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} development membership plans.")]
    private partial void LogSeeded(int count);
}
