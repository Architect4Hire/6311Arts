using System.Net;
using System.Text.Json;
using Arts6311.Tests.Gateway.Infrastructure;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// <c>/api/{version}/internal/**</c> is a 404 from a browser, always (<c>gateway.md</c>, <c>auth.md</c>).
/// </summary>
public sealed class InternalPathTests : IAsyncLifetime
{
    private StubDestination destination = null!;

    public async ValueTask InitializeAsync() => destination = await StubDestination.StartAsync();

    public async ValueTask DisposeAsync() => await destination.DisposeAsync();

    [Theory]
    [InlineData("/api/v1/internal/sessions")]
    [InlineData("/api/v1/internal/sessions/abc")]
    [InlineData("/api/v1/internal")]
    [InlineData("/api/v2/internal/anything")]
    [InlineData("/API/V1/INTERNAL/sessions")]

    // Encoding and path-parameter tricks. Kestrel normalises dot segments and unreserved escapes before routing,
    // so these reach the check already decoded; asserted rather than assumed, because the one that got through
    // would not be obvious.
    [InlineData("/api/v1/./internal/sessions")]
    [InlineData("/api/v1//internal/sessions")]
    [InlineData("/api/v1/internal%2Fsessions")]
    [InlineData("/api/v1/internal;x/sessions")]
    public async Task AnInternalPathIs404AndNeverReachesTheApi(string path)
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // The stronger half of the assertion: the API never saw it. A 404 produced *by the API* would still have
        // told the caller the route exists.
        Assert.Equal(0, destination.Requests);
    }

    [Fact]
    public async Task TheRefusalIsAProblemDetailsWithACorrelationId()
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/internal/sessions", TestContext.Current.CancellationToken);

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        Assert.Equal("request.route_not_found", problem.GetProperty("code").GetString());
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task TheRefusalIsIndistinguishableFromAMissingRoute()
    {
        // The point of 404 over 403: a browser must not be able to tell an internal route from one that was never
        // written. Both answers must carry the same status and the same code.
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var internalPath = await client.GetAsync("/api/v1/internal/sessions", TestContext.Current.CancellationToken);
        var missing = await client.GetAsync("/api/v9/internal/nothing", TestContext.Current.CancellationToken);

        Assert.Equal(internalPath.StatusCode, missing.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/site/health-info")]
    [InlineData("/api/v1/site/internals")]
    [InlineData("/api/internal")]
    public async Task APathThatOnlyLooksInternalIsStillProxied(string path)
    {
        // The block must be precise. `/api/internal` has no version segment, and `internals` is a different
        // segment; over-matching would take real routes off the air. A site path rather than a cms one: the CMS
        // surface needs an editor session from 1.10, and this test is about the internal-path block alone.
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, destination.Requests);
    }
}
