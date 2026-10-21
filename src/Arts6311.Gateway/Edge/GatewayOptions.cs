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
    /// Directive name → extra sources to append to whichever policy a path resolves to, as a space-separated list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For sources an <em>environment</em> needs rather than the application: today, the WordPress origins the site
    /// still reads from while the migration runs, which `appsettings.Development.json` adds and no deployed
    /// environment does (ADR 0039).
    /// </para>
    /// <para>
    /// This exists so that an environment never restates <see cref="Default"/>. A second copy of the policy would
    /// hold a second copy of the inline-script hashes, and the two would drift apart silently — the development
    /// copy masking a stale hash in the shipped one, which is exactly what happened when this was first written as
    /// an override.
    /// </para>
    /// <para>
    /// Only a directive the policy already names is extended. Adding an absent directive would silently narrow what
    /// <c>default-src</c> was covering, which is the opposite of what a line called "additional" should do.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> AdditionalSources { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Merged policies by the policy they came from: <see cref="For"/> runs on every response.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> merged = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the policy for a path: the longest matching prefix, else the default. Ordinal-ignore-case, matching
    /// how ASP.NET compares paths. <see cref="AdditionalSources"/> is then folded in.
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

        return AdditionalSources.Count == 0 ? best : merged.GetOrAdd(best, Extend);
    }

    private string Extend(string policy)
    {
        var directives = policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < directives.Length; i++)
        {
            var name = directives[i].Split(' ', 2)[0];
            if (!AdditionalSources.TryGetValue(name, out var extra) || string.IsNullOrWhiteSpace(extra)) continue;

            foreach (var source in extra.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // A source already allowed is not repeated: a policy is read by people as well as by browsers.
                var sources = directives[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!sources.Contains(source, StringComparer.Ordinal)) directives[i] += " " + source;
            }
        }

        return string.Join("; ", directives);
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
