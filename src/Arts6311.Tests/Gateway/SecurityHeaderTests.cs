using Arts6311.Tests.Gateway.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The headers every response leaves with, and the CSP that differs by route (<c>gateway.md</c>
/// "Security headers").
/// </summary>
public sealed class SecurityHeaderTests : IAsyncLifetime
{
    private StubDestination destination = null!;

    public async ValueTask InitializeAsync() => destination = await StubDestination.StartAsync();

    public async ValueTask DisposeAsync() => await destination.DisposeAsync();

    [Fact]
    public async Task AProxiedResponseCarriesTheSecurityHeaders()
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(response, "Referrer-Policy"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedRequestCarriesThemToo()
    {
        // The headers are set before the pipeline continues, so a response the gateway short-circuits is covered
        // as well as one it proxies. A 404 without a CSP would still be a page a browser renders.
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/internal/sessions", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUpstreamFailureCarriesThemToo()
    {
        // The site cluster has no destination until 3.3, so this is the upstream-failure path. Together with the
        // 404 above it covers both ways a response can be produced without the proxy succeeding.
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Contains("frame-ancestors 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheShippedCspAllowsGoogleFontsForStylesAndFontsOnly()
    {
        // Open Sans comes from Google Fonts, as on spike0 (design-system.md; ADR 0027). The stylesheet host and the
        // font host are allowed in exactly the two directives that load them. Asserted outside Development, because
        // that environment loosens two directives for the WordPress migration (below, ADR 0039).
        var directives = await DirectivesAsync(Environments.Production);

        Assert.Equal(["'self'", "'unsafe-inline'", "https://fonts.googleapis.com"], directives["style-src"]);
        Assert.Equal(["'self'", "https://fonts.gstatic.com"], directives["font-src"]);
        Assert.Equal(["'self'"], directives["connect-src"]);
        Assert.Equal(["'self'", "data:", "blob:"], directives["img-src"]);
    }

    [Fact]
    public async Task ScriptsAreSameOriginPlusTheHashesOfTheInlineScriptsTheSiteRenders()
    {
        // ADR 0039: the SSR site emits three inline scripts a browser executes — the `js` class on <html>, Angular's
        // event-dispatch contract and the __jsaction_bootstrap call that arms event replay. They are allowed by
        // hash, never by 'unsafe-inline', and never by a per-response nonce, which cached HTML cannot carry.
        var directives = await DirectivesAsync(Environments.Production);
        var scriptSrc = directives["script-src"];

        Assert.Equal("'self'", scriptSrc[0]);
        Assert.Equal(3, scriptSrc.Count(source => source.StartsWith("'sha256-", StringComparison.Ordinal)));
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
        Assert.DoesNotContain("'unsafe-eval'", scriptSrc);
    }

    [Fact]
    public async Task NoEnvironmentAllowsTheWordPressOrigins()
    {
        // Development once allowed the live WordPress and its Jetpack CDN while the site still read from them
        // (ADR 0039). The site now reads /api/v1/site/** and serves its own images, so neither origin belongs in
        // any policy, local or shipped.
        foreach (var environment in new[] { Environments.Development, Environments.Production })
        {
            var directives = await DirectivesAsync(environment);

            Assert.DoesNotContain("https://6311arts.com", directives["connect-src"]);
            Assert.DoesNotContain("https://i0.wp.com", directives["img-src"]);
        }
    }

    [Fact]
    public async Task AnAdditionalSourceOnlyExtendsADirectiveThePolicyAlreadyNames()
    {
        // GatewayOptions.Extend's second rule: naming a directive the policy does not have would *create* it, and
        // creating one silently narrows what default-src was covering for it — the opposite of "additional".
        var directives = await DirectivesAsync(
            Environments.Production, ("Gateway:Csp:AdditionalSources:frame-src", "https://frames.test"));

        Assert.False(directives.ContainsKey("frame-src"));
        Assert.DoesNotContain("https://frames.test", directives.SelectMany(entry => entry.Value));
    }

    [Fact]
    public async Task AnAdditionalSourceAlreadyAllowedIsNotRepeated()
    {
        // A policy is read by people as well as by browsers, so the merge does not append a source a directive
        // already lists.
        var directives = await DirectivesAsync(
            Environments.Production, ("Gateway:Csp:AdditionalSources:img-src", "data: https://extra.test"));

        Assert.Equal(1, directives["img-src"].Count(source => source == "data:"));
        Assert.Contains("https://extra.test", directives["img-src"]);
        Assert.Equal(["'self'", "data:", "blob:", "https://extra.test"], directives["img-src"]);
    }

    [Fact]
    public async Task NoEnvironmentRestatesTheDefaultPolicySoTheInlineScriptHashesExistOnce()
    {
        // The failure ADR 0039 records: Development first carried its own copy of Csp:Default, and that copy
        // masked a stale hash in the shipped one. AdditionalSources exists so an environment adds a source
        // without restating the policy, and this is the assertion that keeps it that way.
        var developmentSettings = await File.ReadAllTextAsync(
            Path.Combine(RepoPaths.SourceRoot(), "Arts6311.Gateway", "appsettings.Development.json"),
            TestContext.Current.CancellationToken);

        using var document = System.Text.Json.JsonDocument.Parse(developmentSettings);
        var restatesDefault = document.RootElement.TryGetProperty("Gateway", out var gateway)
            && gateway.TryGetProperty("Csp", out var csp)
            && csp.TryGetProperty("Default", out _);

        Assert.False(
            restatesDefault,
            "appsettings.Development.json restates Gateway:Csp:Default. Add sources through "
                + "Gateway:Csp:AdditionalSources instead, or its copy of the script hashes will drift (ADR 0039).");

        // And the hashes a browser is actually told to trust are the same set in both environments.
        var development = await DirectivesAsync(Environments.Development);
        var shipped = await DirectivesAsync(Environments.Production);

        Assert.Equal(Hashes(shipped["script-src"]), Hashes(development["script-src"]));
        Assert.NotEmpty(Hashes(shipped["script-src"]));

        static string[] Hashes(string[] sources) =>
            [.. sources.Where(source => source.StartsWith("'sha256-", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
    }

    /// <summary>The policy a response really carries, split into directive → sources.</summary>
    private async Task<Dictionary<string, string[]>> DirectivesAsync(
        string environmentName, params (string Key, string Value)[] settings)
    {
        using var gateway = new GatewayFactory(destination, environmentName);
        foreach (var (key, value) in settings)
        {
            gateway.With(key, value);
        }

        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        return Header(response, "Content-Security-Policy")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToDictionary(directive => directive.Split(' ')[0], directive => directive.Split(' ')[1..]);
    }

    [Fact]
    public async Task TheCspDiffersBetweenCheckoutAndOtherPages()
    {
        // gateway.md requires the Square Web Payments SDK origins on checkout and member billing *only*. The real
        // origins arrive with 5.14; what is proven here is the mechanism that will carry them — a per-prefix
        // override, so that prompt adds configuration rather than code.
        const string CheckoutPolicy = "default-src 'self'; script-src 'self' https://web.squarecdn.test";

        using var gateway = new GatewayFactory(destination)
            .With("Gateway:Csp:Overrides:/checkout", CheckoutPolicy);
        using var client = gateway.CreateJsonClient();

        var checkout = await client.GetAsync("/checkout/pay", TestContext.Current.CancellationToken);
        var elsewhere = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        Assert.Equal(CheckoutPolicy, Header(checkout, "Content-Security-Policy"));
        Assert.NotEqual(CheckoutPolicy, Header(elsewhere, "Content-Security-Policy"));
        Assert.DoesNotContain(
            "squarecdn", Header(elsewhere, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLongestMatchingPrefixWins()
    {
        // So a narrower rule can tighten or loosen a broader one rather than being shadowed by declaration order.
        using var gateway = new GatewayFactory(destination)
            .With("Gateway:Csp:Overrides:/members", "default-src 'self'; script-src 'self'")
            .With("Gateway:Csp:Overrides:/members/billing", "default-src 'self'; script-src 'self' https://pay.test");
        using var client = gateway.CreateJsonClient();

        var billing = await client.GetAsync("/members/billing", TestContext.Current.CancellationToken);
        var members = await client.GetAsync("/members/profile", TestContext.Current.CancellationToken);

        Assert.Contains("pay.test", Header(billing, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.DoesNotContain("pay.test", Header(members, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HstsIsNotSentInDevelopment()
    {
        // A local HSTS header would pin localhost to HTTPS in the developer's browser for the max-age, which is a
        // nuisance to undo.
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task HstsIsConfiguredOutsideDevelopment()
    {
        // Over plain HTTP the middleware deliberately writes nothing, so what is asserted is that the gateway
        // starts with HSTS in the pipeline and still serves — the header itself needs a TLS request, which
        // `aspire run` and Azure provide.
        using var gateway = new GatewayFactory(destination, environmentName: Environments.Production);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;
}
