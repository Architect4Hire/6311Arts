using Arts6311.Gateway.Edge;
using Arts6311.Gateway.Proxy;
using Arts6311.Gateway.Session;
using Arts6311.Tests.Gateway.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The route table in <c>appsettings.json</c> against the table in <c>gateway.md</c>, read from the bound YARP
/// configuration rather than from the JSON text — so a typo that parses but means something else still fails.
/// </summary>
public sealed class GatewayRouteTableTests : IDisposable
{
    private readonly GatewayFactory gateway = new();

    [Theory]
    [InlineData("webhooks-square", "api", "/webhooks/square")]
    [InlineData("media", "api", "/media/{**rest}")]
    [InlineData("cms", "api", "/api/v1/cms/{**rest}")]
    [InlineData("cms-media-upload", "api", "/api/v1/cms/media")]
    [InlineData("api", "api", "/api/{**rest}")]
    [InlineData("admin", "admin", "/admin/{**rest}")]
    [InlineData("site", "site", "/{**rest}")]
    public void EveryRowOfTheTableIsRouted(string routeId, string clusterId, string path)
    {
        var route = Route(routeId);

        Assert.Equal(clusterId, route.ClusterId);
        Assert.Equal(path, route.Match.Path);
    }

    [Fact]
    public void TheTableHasNothingBeyondTheDocumentedRows()
    {
        // An extra route is as much a problem as a missing one: it is public surface nobody reviewed.
        Assert.Equal(
            ["admin", "api", "cms", "cms-media-upload", "media", "site", "webhooks-square"],
            Config().Routes.Select(route => route.RouteId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheWebhookRouteIsPostOnlyAndSizeLimited()
    {
        // gateway.md: "POST only, 256 KB limit, no session, rate limited". The limit matters because the body is
        // attacker-controlled and signature-verified only after it has been read.
        var route = Route("webhooks-square");

        Assert.Equal(["POST"], route.Match.Methods);
        Assert.Equal(256 * 1024, route.MaxRequestBodySize);
        Assert.Equal(RateLimitExtensions.Policies.Webhooks, route.RateLimiterPolicy);
    }

    [Fact]
    public void TheMediaUploadRouteIsAPostWithTheUploadLimit()
    {
        // gateway.md "Uploads are streamed": the one CMS route allowed a large body, sized from the same constant
        // the API's RequestSizeLimit uses, so the two cannot drift apart. Everything else under /api/v1/cms/** keeps
        // the server default.
        var route = Route("cms-media-upload");

        Assert.Equal(["POST"], route.Match.Methods);
        Assert.Equal(Arts6311.Domain.Modules.Media.Managers.MediaPolicy.MaxUploadRequestBytes, route.MaxRequestBodySize);
        Assert.Equal(GatewayPolicies.CmsEditor, route.AuthorizationPolicy);
        Assert.Equal(RateLimitExtensions.Policies.Api, route.RateLimiterPolicy);
    }

    [Fact]
    public void MediaDeliveryIsReadOnly()
    {
        // /media/** serves bytes and nothing else; there is no write behind it to reach.
        Assert.Equal(["GET", "HEAD"], Route("media").Match.Methods);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("cms")]
    [InlineData("cms-media-upload")]
    public void TheCmsSurfaceNeedsAnEditorSession(string routeId)
    {
        // gateway.md: "/admin/** and /api/v1/cms/** need a CmsEditor session". Declared on the route, so the
        // gate is in the reviewed table rather than in a path list somewhere in code.
        Assert.Equal(GatewayPolicies.CmsEditor, Route(routeId).AuthorizationPolicy);
    }

    [Fact]
    public void TheCmsRouteIsMoreSpecificThanTheApiRoute()
    {
        // Precedence is ASP.NET's most-specific-wins, so /api/v1/cms/** matches this route and not the open
        // /api/** one. If that ever stopped being true, the CMS surface would quietly become anonymous.
        var cms = Route("cms").Match.Path!;
        var api = Route("api").Match.Path!;

        Assert.StartsWith(api.Replace("{**rest}", string.Empty, StringComparison.Ordinal), cms, StringComparison.Ordinal);
        Assert.True(cms.Length > api.Length);
    }

    [Fact]
    public void EveryAuthorizedRouteToTheApiCarriesTheInternalToken()
    {
        // A route that requires a session but is not token-bearing would reach the API as an anonymous request,
        // and the 401 would come back pointing nowhere near the cause. Only the API routes: the admin route
        // needs a session to serve the CMS bundle, and a bundle host has no use for a credential (ADR 0022).
        var authorized = Config().Routes
            .Where(route => route.ClusterId == "api")
            .Where(route => route.AuthorizationPolicy is { Length: > 0 } and not "anonymous")
            .Select(route => route.RouteId);

        Assert.All(authorized, routeId => Assert.Contains(
            routeId, GatewayTransforms.TokenBearingRoutes, StringComparer.Ordinal));
    }

    [Fact]
    public void TheApiRouteIsRateLimited()
    {
        Assert.Equal(RateLimitExtensions.Policies.Api, Route("api").RateLimiterPolicy);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("site")]
    public void EveryClusterResolvesThroughServiceDiscovery(string clusterId)
    {
        // "https+http://<name>" is the scheme-priority form: https first, then http. No host or port appears
        // anywhere in the repo (aspire.md).
        var cluster = Config().Clusters.Single(cluster => cluster.ClusterId == clusterId);
        var destination = Assert.Single(cluster.Destinations!);

        Assert.Equal($"https+http://{clusterId}", destination.Value.Address);
    }

    [Fact]
    public void NoRouteSendsBrowsersToAnInternalPath()
    {
        // The internal paths are blocked by middleware, so no route may quietly offer a way to them.
        Assert.DoesNotContain(
            Config().Routes,
            route => route.Match.Path?.Contains("internal", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void EveryRateLimiterPolicyNamedByARouteIsDefined()
    {
        // A route naming a policy that configuration never defines would fail on that route only, at request time.
        var named = Config().Routes
            .Select(route => route.RateLimiterPolicy)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Assert.All(named, policy => Assert.Contains(policy, RateLimitExtensions.Policies.All));
    }

    private RouteConfig Route(string routeId) =>
        Config().Routes.Single(route => route.RouteId == routeId);

    private IProxyConfig Config() =>
        gateway.Services.GetServices<IProxyConfigProvider>().Single().GetConfig();

    public void Dispose() => gateway.Dispose();
}
