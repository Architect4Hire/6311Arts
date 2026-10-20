using System.Security.Claims;

namespace Arts6311.Api.Authentication;

/// <summary>Reads the internal token's claims (ADR 0002) the way controllers need them.</summary>
internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The signed-in person's user id. Only called on routes whose policy requires a user token, and every product
    /// policy requires <c>sub</c> (<c>ApiPolicies</c>), so its absence is a fault, not a refusal.
    /// </summary>
    public static string UserId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.FindFirstValue(InternalTokenDefaults.SubjectClaim)
            ?? throw new InvalidOperationException("A user route was reached without a subject claim.");
    }

    /// <summary>
    /// Whether the caller is signed-in CMS staff (Admin or Editor) on a user token. For anonymous routes that show
    /// staff more — draft media — without requiring a session.
    /// </summary>
    public static bool IsCmsStaff(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Identity?.IsAuthenticated == true
            && user.HasClaim(InternalTokenDefaults.TokenUseClaim, InternalTokenDefaults.UserTokenUse)
            && (user.IsInRole(ApiPolicies.AdminRole) || user.IsInRole(ApiPolicies.EditorRole));
    }
}
