namespace Arts6311.Gateway.Edge;

/// <summary>
/// Edge policy, all from configuration (<c>gateway.md</c>). Nothing here is a literal in code: CSP, rate limits
/// and trusted proxies all differ between a laptop and Azure.
/// </summary>
internal sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>
    /// Proxies whose <c>X-Forwarded-*</c> headers may be believed, as IP addresses or CIDR ranges.
    /// </summary>
    /// <remarks>
    /// Empty by default, and that is the safe default rather than an oversight: in run mode nothing sits in front
    /// of the gateway, so any <c>X-Forwarded-For</c> on an inbound request was put there by the client. 6.1
    /// populates this with Azure Container Apps' ingress.
    /// </remarks>
    public IList<string> TrustedProxies { get; } = [];

    public CspOptions Csp { get; } = new();

    /// <summary>Named rate-limit policies (<c>gateway.md</c>), keyed by policy name.</summary>
    public IDictionary<string, RateLimitPolicyOptions> RateLimits { get; } =
        new Dictionary<string, RateLimitPolicyOptions>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The generous limit every request passes through, whatever route it matched.</summary>
    public RateLimitPolicyOptions GlobalRateLimit { get; } = new();
}

internal sealed class CspOptions
{
    /// <summary>The policy every response gets unless a longer prefix in <see cref="Overrides"/> matches.</summary>
    public string Default { get; set; } = string.Empty;

    /// <summary>
    /// Path prefix → policy, longest prefix wins. This is how the Square Web Payments SDK origins reach checkout
    /// and member billing, and only those, in 5.14 — as configuration, with no code change here.
    /// </summary>
    public IDictionary<string, string> Overrides { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Referrer { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>
    /// Resolves the policy for a path: the longest matching prefix, else the default. Ordinal-ignore-case, matching
    /// how ASP.NET compares paths.
    /// </summary>
    public string For(PathString path)
    {
        var best = Default;
        var bestLength = -1;

        foreach (var (prefix, policy) in Overrides)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > bestLength)
            {
                (best, bestLength) = (policy, prefix.Length);
            }
        }

        return best;
    }
}

/// <summary>One fixed window: how many requests a client may make, and over what period.</summary>
internal sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; }

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Requests held rather than refused once the window is full. Zero means refuse immediately.</summary>
    public int QueueLimit { get; set; }
}
