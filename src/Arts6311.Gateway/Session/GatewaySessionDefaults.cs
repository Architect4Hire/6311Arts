namespace Arts6311.Gateway.Session;

internal static class GatewaySessionDefaults
{
    /// <summary>
    /// The gateway's only authentication scheme. There is no cookie-authentication middleware and no OIDC
    /// handler: the session is a server-side ticket the gateway reads itself (ADR 0002).
    /// </summary>
    public const string Scheme = "GatewaySession";
}

/// <summary>The gateway's route authorization policies (<c>gateway.md</c>).</summary>
internal static class GatewayPolicies
{
    /// <summary>
    /// A session carrying <c>Admin</c> or <c>Editor</c>: what <c>/admin/**</c> and <c>/api/v1/cms/**</c> need.
    /// Coarse on purpose — the API checks the same roles again, and anything finer is a data-dependent rule,
    /// which belongs in the domain rather than at the edge (<c>gateway.md</c> "Not the gateway's job").
    /// </summary>
    public const string CmsEditor = "CmsEditor";

    /// <summary>Any signed-in person: the BFF's own authenticated endpoints.</summary>
    public const string Session = "Session";
}
