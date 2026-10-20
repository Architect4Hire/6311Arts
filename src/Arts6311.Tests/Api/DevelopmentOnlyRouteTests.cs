using System.Net;
using Arts6311.Tests.Api.Infrastructure;

namespace Arts6311.Tests.Api;

/// <summary>
/// Development-only scaffolding must not exist outside Development, through the real pipeline — which is the only
/// place the claim can be checked, because authorization runs before any action code.
/// </summary>
/// <remarks>
/// Prompt 1.5 gated the ping route with an <c>IHostEnvironment</c> check inside the action. That ran after
/// authorization, so outside Development an anonymous caller got 401 and an admin got 404: the route existed, and
/// said so. These tests exist to keep that from coming back (ADR 0021). The ping route was deleted in 1.12, so the
/// gate is exercised through the test host's <see cref="DevelopmentOnlyProbeController"/>.
/// </remarks>
public sealed class DevelopmentOnlyRouteTests
{
    private const string DevelopmentOnlyRoute = "/" + DevelopmentOnlyProbeController.Route;

    [Fact]
    public async Task OutsideDevelopmentADevelopmentOnlyRouteIs404WithNoToken()
    {
        using var api = new ProductionApiFactory();
        using var client = api.CreateJsonClient();

        // 404, not 401: an unauthenticated caller must not be able to tell the route apart from one that was never
        // written. A 401 here would confirm its existence.
        var response = await client.PostAsync(
            DevelopmentOnlyRoute, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OutsideDevelopmentADevelopmentOnlyRouteIs404EvenForAnAdmin()
    {
        using var api = new ProductionApiFactory();
        using var client = api.CreateJsonClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, DevelopmentOnlyRoute);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {api.Tokens.UserToken("Admin")}");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, api.Probe.Calls);
    }

    [Fact]
    public async Task InDevelopmentADevelopmentOnlyRouteExists()
    {
        // The counterpart: without this, removing the controller everywhere would also pass the tests above.
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, DevelopmentOnlyRoute);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {api.Tokens.UserToken("Admin")}");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, api.Probe.Calls);
    }

    [Fact]
    public async Task TheGateIsSelective()
    {
        // Proves the gate removes only marked controllers rather than controllers wholesale outside Development:
        // both the public site route and the unmarked probe survive.
        using var api = new ProductionApiFactory();
        using var client = api.CreateJsonClient();

        var site = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);
        var unmarked = await client.PostAsync(
            "/" + CmsAdminProbeController.Route, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, site.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unmarked.StatusCode);
    }
}
