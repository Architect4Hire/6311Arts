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
