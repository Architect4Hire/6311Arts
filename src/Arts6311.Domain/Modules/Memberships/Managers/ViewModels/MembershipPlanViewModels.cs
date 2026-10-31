using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Memberships.Managers.ViewModels;

/// <summary>
/// A new membership tier. It starts as a Draft; publishing is its own command.
/// </summary>
/// <remarks>
/// No price or cadence <em>projection</em> appears on this model or any other write model in this module: those are
/// Square's record (square.md, ADR 0005). <c>StartingPriceCents</c> and <c>StartingCadence</c> are the CMS's own
/// draft fields, the values the first Square push creates the plan with.
/// </remarks>
/// <param name="Slug">Optional: made from <paramref name="Name"/> when left out.</param>
/// <param name="Perks">What the tier includes, in the order the page should list them.</param>
/// <param name="DiscountPercent">What a member saves on every class, as whole percent. 0 when left out.</param>
/// <param name="Featured">Whether the page highlights this tier.</param>
/// <param name="DisplayOrder">Optional: after every existing tier when left out.</param>
/// <param name="StartingCadence">Optional: <c>Annual</c> when left out, which is what every tier bills at today.</param>
public sealed record MembershipPlanCreateViewModel(
    string Name,
    string? Slug = null,
    IReadOnlyList<string>? Perks = null,
    int? DiscountPercent = null,
    bool? Featured = null,
    int? DisplayOrder = null,
    long? StartingPriceCents = null,
    MembershipCadence? StartingCadence = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged. Status is not here: it changes only through the
/// publish, unpublish, schedule and archive commands (content.md).
/// </summary>
/// <param name="Slug">A plan has no page of its own, so changing it redirects nothing (ADR 0040).</param>
/// <param name="Perks">Replaces the perks, in order; an empty list clears them.</param>
/// <param name="StartingPriceCents">Refused once Square has reported money or a cadence: from then on price is Square's (ADR 0005).</param>
/// <param name="RemoveStartingPrice"><c>true</c> clears it; not allowed together with <paramref name="StartingPriceCents"/>.</param>
/// <param name="StartingCadence">Refused once Square holds the plan, on the same gate.</param>
public sealed record MembershipPlanUpdateViewModel(
    string? Name = null,
    string? Slug = null,
    IReadOnlyList<string>? Perks = null,
    int? DiscountPercent = null,
    bool? Featured = null,
    int? DisplayOrder = null,
    long? StartingPriceCents = null,
    bool? RemoveStartingPrice = null,
    MembershipCadence? StartingCadence = null);

/// <summary>A page of membership plans, in the order the site shows them.</summary>
/// <param name="Search">Part of a name, matched without regard to case.</param>
/// <param name="Sort">
/// order, tier, status, price, discount, updated — with a leading <c>-</c> for descending (ADR 0062). The Deleted
/// view ignores it.
/// </param>
public sealed record MembershipPlanListViewModel(
    string? Cursor = null,
    int? Limit = null,
    PublishStatus? Status = null,
    string? Search = null,
    string? Sort = null);
