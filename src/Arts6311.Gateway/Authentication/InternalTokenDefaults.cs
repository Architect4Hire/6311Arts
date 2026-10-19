namespace Arts6311.Gateway.Authentication;

/// <summary>
/// The internal token's fixed shape (ADR 0002, <c>auth.md</c> "Gateway → API trust"). The signing half.
/// </summary>
/// <remarks>
/// A deliberate mirror of <c>Arts6311.Api/Authentication/InternalTokenDefaults.cs</c>. The gateway cannot
/// reference the API — <c>ProjectReferenceTests</c> pins it to ServiceDefaults only, because the gateway holds no
/// domain code — and ServiceDefaults owns telemetry and health, not auth, so neither project can host a shared
/// copy without widening its charter (ADR 0022). <c>InternalTokenContractTests</c> compares the two copies field
/// by field and signs a token that the API's own validation parameters must accept: a drift between them would
/// otherwise surface only as every internal request failing its audience or signature check in production.
/// </remarks>
internal static class InternalTokenDefaults
{
    public const string Issuer = "6311-gateway";

    public const string Audience = "6311-api";

    /// <summary>What the token is for. Product routes take only <c>user</c>; internal routes only <c>service</c>.</summary>
    public const string TokenUseClaim = "token_use";

    public const string UserTokenUse = "user";

    public const string ServiceTokenUse = "service";

    /// <summary>Short, not the SOAP-era URI: the API reads it with <c>MapInboundClaims</c> off.</summary>
    public const string RoleClaim = "role";

    public const string SubjectClaim = "sub";

    public const string SessionIdClaim = "sid";

    /// <summary>
    /// The lifetime every token gets. The API rejects anything longer, and also rejects a token with no
    /// <c>nbf</c>, so the signer always sets one (ADR 0021).
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
}
