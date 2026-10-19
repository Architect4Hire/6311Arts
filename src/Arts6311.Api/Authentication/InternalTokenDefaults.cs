namespace Arts6311.Api.Authentication;

/// <summary>
/// The internal token's fixed shape (ADR 0002, <c>.claude/rules/auth.md</c> "Gateway → API trust"). The gateway
/// signs; the API verifies. Both halves have to agree on every value here, so nothing below is configuration.
/// </summary>
/// <remarks>
/// The gateway cannot reference this assembly — <c>ProjectReferenceTests</c> pins it to ServiceDefaults only — so
/// prompt 1.8 mirrors these constants on the signing side, and <c>InternalTokenContractTests</c> (which references
/// both projects) asserts the two copies agree. A mismatch would otherwise show up only as every internal request
/// failing its signature or audience check with no obvious cause.
/// </remarks>
internal static class InternalTokenDefaults
{
    /// <summary>The one authentication scheme the API has. There is deliberately no second one to fall back to.</summary>
    public const string Scheme = "InternalToken";

    public const string Issuer = "6311-gateway";

    public const string Audience = "6311-api";

    /// <summary>
    /// The claim saying what the token is for. Product routes take only <see cref="UserTokenUse"/>;
    /// <c>/api/v1/internal/**</c> takes only <see cref="ServiceTokenUse"/>.
    /// </summary>
    public const string TokenUseClaim = "token_use";

    public const string UserTokenUse = "user";

    public const string ServiceTokenUse = "service";

    /// <summary>The claim carrying a user's roles. Short, not the SOAP-era URI: <c>MapInboundClaims</c> is off.</summary>
    public const string RoleClaim = "role";

    /// <summary>The claim carrying the user id.</summary>
    public const string SubjectClaim = "sub";

    /// <summary>The claim carrying the gateway's session id.</summary>
    public const string SessionIdClaim = "sid";

    /// <summary>
    /// The longest lifetime a token may claim. A token whose signature is valid but which asks to live an hour is
    /// rejected: the ≤ 2 minute rule is what keeps a captured token nearly worthless.
    /// </summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(2);
}
