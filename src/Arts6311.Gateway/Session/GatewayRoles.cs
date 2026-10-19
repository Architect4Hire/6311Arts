namespace Arts6311.Gateway.Session;

/// <summary>
/// The three role names, as the gateway must spell them.
/// </summary>
/// <remarks>
/// A deliberate mirror of <c>Arts6311.Domain/Modules/Identity/Managers/IdentityRoles.cs</c>, for the same reason
/// <c>InternalTokenDefaults</c> is duplicated: the gateway holds no domain code and cannot reference the Domain
/// (ADR 0022). <c>SessionContractTests</c> compares the two copies, because a drift here would not fail a build —
/// it would silently stop a role from reaching <c>/admin</c>, or stop a staff session from getting staff
/// lifetimes.
/// </remarks>
internal static class GatewayRoles
{
    public const string Admin = "Admin";

    public const string Editor = "Editor";

    public const string Member = "Member";

    /// <summary>The roles that reach <c>/admin</c> and the CMS API, and that carry the shorter lifetimes.</summary>
    public static readonly string[] Staff = [Admin, Editor];

    public static bool IsStaff(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return roles.Any(role => Staff.Contains(role, StringComparer.Ordinal));
    }
}
