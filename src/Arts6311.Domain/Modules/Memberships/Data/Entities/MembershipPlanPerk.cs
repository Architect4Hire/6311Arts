namespace Arts6311.Domain.Modules.Memberships.Data.Entities;

/// <summary>One line of what a membership tier includes: “5% off every class &amp; workshop”.</summary>
/// <remarks>
/// Ordered rows rather than a JSON column (ADR 0040), as a shop item's gallery is: the position is the key beside
/// the plan, so a reorder rewrites the text in place and one save never has to delete a row and add it back.
/// </remarks>
public sealed class MembershipPlanPerk
{
    public Guid MembershipPlanId { get; set; }

    /// <summary>Where it comes in the list, from 0.</summary>
    public int Position { get; set; }

    public string Text { get; set; } = string.Empty;
}
