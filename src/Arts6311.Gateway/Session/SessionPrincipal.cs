using System.Security.Claims;
using Arts6311.Gateway.Authentication;

namespace Arts6311.Gateway.Session;

/// <summary>
/// Turns a ticket into the principal the rest of the pipeline reads: route authorization, and the internal token
/// the proxy signs from it.
/// </summary>
/// <remarks>
/// The claim types are the internal token's short ones (<c>sub</c>, <c>sid</c>, <c>role</c>), not the SOAP-era
/// URIs, so <see cref="InternalTokenSigner"/> finds them without translation and the API reads them back with
/// <c>MapInboundClaims</c> off (ADR 0021).
/// </remarks>
internal static class SessionPrincipal
{
    /// <summary>The claim the display name travels on. Never a credential and never an email address.</summary>
    public const string NameClaim = "name";

    public static ClaimsPrincipal From(SessionTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var claims = new List<Claim>
        {
            new(InternalTokenDefaults.SubjectClaim, ticket.UserId),
            new(InternalTokenDefaults.SessionIdClaim, ticket.SessionId),
            new(NameClaim, ticket.DisplayName),
        };

        claims.AddRange(ticket.Roles.Select(role => new Claim(InternalTokenDefaults.RoleClaim, role)));

        // The role type is declared on the identity, which is what makes RequireRole("Admin", "Editor") read
        // these claims rather than looking for the URI-shaped default.
        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: GatewaySessionDefaults.Scheme,
            nameType: NameClaim,
            roleType: InternalTokenDefaults.RoleClaim));
    }
}
