using System.Text.Json.Serialization;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;

/// <summary>One membership tier in full, as the CMS edits it.</summary>
/// <param name="Perks">What the tier includes, in the order the page lists them.</param>
/// <param name="DiscountPercent">What a member saves on every class, as whole percent.</param>
/// <param name="DisplayOrder">Where it comes among the tiers.</param>
/// <param name="StartingPriceCents">The CMS's draft price, the value the first Square push creates the plan with.</param>
/// <param name="StartingCadence">The CMS's draft cadence, sent with that same push (ADR 0005).</param>
/// <param name="Currency">The currency every amount here is in (api-contract.md).</param>
/// <param name="StartingPriceEditable">
/// Whether <paramref name="StartingPriceCents"/> and <paramref name="StartingCadence"/> can still be edited:
/// <c>false</c> once Square has reported money or a cadence, after which both are Square's (ADR 0005, 0040).
/// </param>
/// <param name="PriceCents">Square's record of what the plan bills. <c>null</c> until Square has reported it.</param>
/// <param name="Cadence">Square's record of how often it bills, in Square's own words. <c>null</c> until the first pull.</param>
/// <param name="SquareHasRecord">Whether Square has reported money or a cadence for this plan yet.</param>
/// <param name="SitePath">Where the site shows it: the memberships page, which every plan shares (ADR 0040).</param>
/// <param name="LegacyWordPressId">The WooCommerce membership it was imported from, for provenance.</param>
/// <param name="UpdatedBy">The user id behind the last write.</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
public sealed record MembershipPlanServiceModel(
    Guid Id,
    string Name,
    string Slug,
    IReadOnlyList<string> Perks,
    int DiscountPercent,
    bool Featured,
    int DisplayOrder,
    long? StartingPriceCents,
    MembershipCadence StartingCadence,
    string Currency,
    bool StartingPriceEditable,
    long? PriceCents,
    string? Cadence,
    bool SquareHasRecord,
    PublishStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledFor,
    string SitePath,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>
/// One row of the membership list. It carries what Square says about money and billing, because that is what staff
/// scan the list for.
/// </summary>
/// <param name="Currency">The currency the amounts here are in (api-contract.md).</param>
public sealed record MembershipPlanSummaryServiceModel(
    Guid Id,
    string Name,
    string Slug,
    PublishStatus Status,
    int DiscountPercent,
    bool Featured,
    int DisplayOrder,
    int PerkCount,
    long? StartingPriceCents,
    long? PriceCents,
    string? Cadence,
    string Currency,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One published tier as <c>/memberships</c> shows it (ADR 0040, 0042): the CMS's copy and Square's record of the
/// money, with no starting figures, status or staff bookkeeping.
/// </summary>
/// <param name="Perks">In the order staff set.</param>
/// <param name="DiscountPercent">The class discount members get.</param>
/// <param name="PriceCents">Square's record of the price; <c>null</c> until Square has reported one.</param>
/// <param name="Cadence">Square's word for how often it bills; <c>null</c> until Square has reported one.</param>
public sealed record SiteMembershipPlanServiceModel(
    Guid Id,
    string Slug,
    string Name,
    IReadOnlyList<string> Perks,
    int DiscountPercent,
    bool Featured,
    long? PriceCents,
    string Currency,
    string? Cadence);
