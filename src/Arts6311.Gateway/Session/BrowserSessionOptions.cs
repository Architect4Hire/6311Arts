namespace Arts6311.Gateway.Session;

/// <summary>
/// The browser session's shape and lifetimes, all from configuration (<c>auth.md</c> "Lifetimes": "All values
/// are configuration").
/// </summary>
internal sealed class BrowserSessionOptions
{
    public const string SectionName = "Gateway:Session";

    /// <summary>
    /// The cookie that carries the ticket reference. <c>__Host-</c> is part of the security property, not a
    /// naming convention: the prefix makes a browser refuse the cookie unless it is <c>Secure</c>, <c>Path=/</c>
    /// and host-only, so a sibling subdomain cannot set one for us.
    /// </summary>
    public string CookieName { get; set; } = "__Host-6311.session";

    /// <summary>Sessions carrying <c>Admin</c> or <c>Editor</c>: 30 minutes idle, 8 hours absolute (ADR 0018).</summary>
    public BrowserSessionLifetimeOptions Staff { get; } = new();

    /// <summary>Everyone else. These are the 1.10 defaults <c>auth.md</c> defers to.</summary>
    public BrowserSessionLifetimeOptions Member { get; } = new();

    /// <summary>
    /// How long a ticket may go without its security stamp being checked against the API. This is the window in
    /// which a password reset, a role change or "log out everywhere" has not yet reached a live session
    /// (<c>auth.md</c> "Revalidation"), traded against an internal call per session per interval.
    /// </summary>
    public TimeSpan RevalidationInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Where a browser navigation without a session is sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A local path: it is written into a redirect, so anything absolute would be an open redirect waiting to be
    /// configured.
    /// </para>
    /// <para>
    /// <strong>It must not be a path that itself needs a session.</strong> The obvious choice, <c>/admin/login</c>,
    /// is inside the prefix <c>/admin/**</c> that requires a <c>CmsEditor</c> session — a browser sent there
    /// would be redirected again, forever, and its scripts and stylesheets would be refused besides, so the page
    /// could not render even if the document were let through. The sign-in page therefore lives on the public
    /// site (ADR 0024), and <see cref="Validate"/> refuses a path under a gated prefix.
    /// </para>
    /// </remarks>
    public string LoginPath { get; set; } = "/signin";

    /// <summary>
    /// The prefixes a <see cref="LoginPath"/> may not sit under, because they need the session the login page
    /// exists to establish (<c>gateway.md</c>).
    /// </summary>
    private static readonly string[] GatedPrefixes = ["/admin", "/api"];

    /// <summary>
    /// Throws unless every value is usable. Called while composing services, so a nonsensical lifetime stops the
    /// host instead of producing sessions that never expire — the pattern 1.7 set for the signing key and 1.8 for
    /// the rate-limit policies.
    /// </summary>
    public void Validate()
    {
        if (!CookieName.StartsWith("__Host-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(CookieName)} must start with '__Host-' (auth.md). The prefix is what makes "
                + "a browser refuse the cookie unless it is Secure, host-only and Path=/.");
        }

        Staff.Validate($"{SectionName}:{nameof(Staff)}");
        Member.Validate($"{SectionName}:{nameof(Member)}");

        if (RevalidationInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(RevalidationInterval)} must be greater than zero. A session that is never "
                + "revalidated outlives a password reset.");
        }

        // "//evil.example" and "/\evil.example" both begin with '/' and both send a browser to another origin.
        if (!LoginPath.StartsWith('/') || LoginPath.StartsWith("//", StringComparison.Ordinal)
            || LoginPath.StartsWith("/\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(LoginPath)} must be a local path beginning with a single '/', not an "
                + "absolute or protocol-relative URL: it is written into a redirect.");
        }

        if (GatedPrefixes.Any(prefix =>
            LoginPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
            || LoginPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(LoginPath)} is '{LoginPath}', which is behind the session gate it exists "
                + "to get past. A browser sent there would be redirected to it again, forever.");
        }
    }

    /// <summary>
    /// The lifetimes for a ticket holding these roles. Staff wins whenever a role list contains one: the shorter
    /// window is the compensating control for having no second factor (ADR 0018), so a person who is both a
    /// member and an editor gets the editor's.
    /// </summary>
    public BrowserSessionLifetimeOptions LifetimeFor(IEnumerable<string> roles) =>
        GatewayRoles.IsStaff(roles) ? Staff : Member;
}

/// <summary>One kind of session's two clocks.</summary>
internal sealed class BrowserSessionLifetimeOptions
{
    /// <summary>How long the session survives without a request. Each request restarts it.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long the session may live however busy it is. Nothing restarts this one.</summary>
    public TimeSpan AbsoluteTimeout { get; set; } = TimeSpan.FromHours(8);

    public void Validate(string path)
    {
        if (IdleTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{path}:{nameof(IdleTimeout)} must be greater than zero.");
        }

        if (AbsoluteTimeout < IdleTimeout)
        {
            throw new InvalidOperationException(
                $"{path}:{nameof(AbsoluteTimeout)} must be at least {nameof(IdleTimeout)}; an absolute limit "
                + "shorter than the idle window would make the idle window unreachable.");
        }
    }
}
