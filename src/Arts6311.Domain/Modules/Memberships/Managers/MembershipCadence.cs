using System.Text.Json.Serialization;

namespace Arts6311.Domain.Modules.Memberships.Managers;

/// <summary>
/// How often a membership bills. Stored as its number, so the values never change; written to JSON by name, as
/// <c>PublishStatus</c> is.
/// </summary>
/// <remarks>
/// The CMS offers the two the centre sells, and sends one when the first push creates the Square subscription plan
/// (ADR 0005). It is deliberately <strong>not</strong> the type of <c>MembershipPlan.Cadence</c>: that projection is
/// Square's own word for what the plan bills, and Square has a dozen cadences we do not offer (ADR 0040).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MembershipCadence>))]
public enum MembershipCadence
{
    Monthly = 0,

    /// <summary>What every tier the spike sells bills at, so the default for a new plan.</summary>
    Annual = 10,
}
