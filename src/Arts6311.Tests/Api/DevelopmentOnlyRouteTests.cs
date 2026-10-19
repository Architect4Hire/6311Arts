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
/// said so. These tests exist to keep that from coming back (ADR 0021).
/// </remarks>
public sealed class DevelopmentOnlyRouteTests
{
    private const string PingRoute = "/api/v1/cms/ops/ping";

    [Fact]
    public async Task OutsideDevelopmentThePingRouteIs404WithNoToken()
    {
        using var api = new ProductionApiFactory();
        using var client = api.CreateJsonClient();

        // 404, not 401: an unauthenticated caller must not be able to tell the route apart from one that was never
        // written. A 401 here would confirm its existence.
        var response = await client.PostAsync(PingRoute, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OutsideDevelopmentThePingRouteIs404EvenForAnAdmin()
    {
        using var api = new ProductionApiFactory();
        using var client = api.CreateJsonClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, PingRoute);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {api.Tokens.UserToken("Admin")}");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InDevelopmentThePingRouteExists()
    {
        // The counterpart: without this, removing the controller everywhere would also pass the tests above.
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await client.PostAsync(PingRoute, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ThePublicSiteRouteExistsInBothEnvironments()
    {
        // Proves the gate is selective rather than removing controllers wholesale outside Development.
        using var api = new ProductionApiFactory();
        using var client = api.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
