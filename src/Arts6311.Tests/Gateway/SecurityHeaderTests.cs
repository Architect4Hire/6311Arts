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
    public async Task TheWordPressOriginsAreDevelopmentOnly()
    {
        // The site still reads classes, exhibitions and shop items from the live WordPress while the migration runs
        // (ADR 0039). That is a local crutch: it must never reach a deployed policy, where those reads come from the
        // API on this origin. Both halves are asserted, because a crutch nobody tests is a crutch that ships.
        var development = await DirectivesAsync(Environments.Development);
        var shipped = await DirectivesAsync(Environments.Production);

        Assert.Contains("https://6311arts.com", development["connect-src"]);
        Assert.Contains("https://i0.wp.com", development["img-src"]);

        Assert.DoesNotContain("https://6311arts.com", shipped["connect-src"]);
        Assert.DoesNotContain("https://i0.wp.com", shipped["img-src"]);
    }

    /// <summary>The policy a response really carries, split into directive → sources.</summary>
    private async Task<Dictionary<string, string[]>> DirectivesAsync(string environmentName)
    {
        using var gateway = new GatewayFactory(destination, environmentName);
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
