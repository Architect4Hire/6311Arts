using System.Net;
using System.Text.Json;
using Arts6311.Tests.Gateway.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// What actually arrives at the API (<c>gateway.md</c> "Headers"). Asserted against a real destination rather
/// than against the transforms, because the question is what crossed the wire, not what the gateway intended.
/// </summary>
public sealed class GatewayForwardingTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private StubDestination destination = null!;

    public async ValueTask InitializeAsync() => destination = await StubDestination.StartAsync();

    public async ValueTask DisposeAsync() => await destination.DisposeAsync();

    [Fact]
    public async Task AClientAuthorizationHeaderIsStripped()
    {
        // The gateway is the only thing that may present a credential to the API. A caller's own bearer header must
        // not arrive — forwarding it would let anyone who can mint a token the API trusts bypass the session.
        var received = await Forward(request =>
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer forged"));

        Assert.False(received.Has("Authorization"));
    }

    [Fact]
    public async Task ACookieHeaderIsStripped()
    {
        // The session cookie is the gateway's to read. The API takes no cookies at all (ADR 0002).
        var received = await Forward(request =>
            request.Headers.TryAddWithoutValidation("Cookie", "__Host-6311.session=abc"));

        Assert.False(received.Has("Cookie"));
    }

    [Theory]
    [InlineData("X-Internal-Token")]
    [InlineData("X-Gateway-User")]
    [InlineData("X-Gateway-Session")]
    public async Task InternalHeadersAreStripped(string header)
    {
        // A client setting one of these could otherwise claim to be the gateway acting on someone's behalf.
        var received = await Forward(request => request.Headers.TryAddWithoutValidation(header, "forged"));

        Assert.False(received.Has(header));
    }

    [Fact]
    public async Task ASpoofedForwardedForIsReplacedNotAppended()
    {
        // YARP's X-Forwarded action is Set, so the client's value is overwritten with the real peer address. If it
        // were appended, the API would see the attacker's value first and could not tell which to trust.
        var received = await Forward(request =>
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "9.9.9.9"));

        // The client's claim must be gone. The true peer address is not asserted: the in-memory test server has no
        // socket, so there is no RemoteIpAddress for the transform to write, and a real connection is what
        // `aspire run` exercises. What matters either way is that the forged value cannot survive.
        Assert.DoesNotContain("9.9.9.9", received.Header("X-Forwarded-For") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASpoofedForwardedHostIsReplaced()
    {
        var received = await Forward(request =>
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "evil.example"));

        Assert.DoesNotContain(
            "evil.example", received.Header("X-Forwarded-Host") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnonymousRequestCarriesNoToken()
    {
        // Before 1.10 nothing is authenticated, so nothing should be signed. An anonymous request must reach the
        // API with no credential at all, leaving its [AllowAnonymous] routes to decide (ADR 0021).
        var received = await Forward();

        Assert.False(received.Has("Authorization"));
    }

    [Fact]
    public async Task TheTraceReachesTheDestination()
    {
        // YARP forwards through a bare SocketsHttpHandler, so it does not inject traceparent on its own. Without
        // the transform that does, the gateway's span and the API's would be separate traces and CLAUDE.md's
        // "one trace from gateway → API → SQL" would not hold.
        var received = await Forward();

        var traceParent = received.Header("traceparent");
        Assert.NotNull(traceParent);
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$", traceParent);
    }

    [Fact]
    public async Task AClientTraceParentIsReplacedByTheGatewaysOwn()
    {
        // ASP.NET Core has already parsed the inbound value into the current activity, so the activity is the
        // authority. A client-supplied span id must not be passed through untouched.
        const string Forged = "00-11111111111111111111111111111111-2222222222222222-01";

        var received = await Forward(request => request.Headers.TryAddWithoutValidation("traceparent", Forged));

        Assert.NotEqual(Forged, received.Header("traceparent"));
    }

    [Fact]
    public async Task OrdinaryHeadersStillReachTheDestination()
    {
        // The deny-list must not have become an accidental allow-list: a route that cannot receive Accept or
        // Idempotency-Key is broken.
        var received = await Forward(request =>
        {
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "abc-123");
        });

        Assert.Equal("application/json", received.Header("Accept"));
        Assert.Equal("abc-123", received.Header("Idempotency-Key"));
    }

    [Fact]
    public void OnlyTheApiClusterIsEligibleForAToken()
    {
        // The token transform is added per route, from the cluster id, so the site and admin apps can never be
        // handed an internal token. A credential given to a process with no use for it is one more place it can be
        // logged or passed on. Asserted on the built transforms, because no route is authenticated before 1.10.
        using var gateway = new GatewayFactory(destination);

        var tokenBearingClusters = gateway.Services
            .GetServices<IProxyConfigProvider>()
            .Single()
            .GetConfig()
            .Routes
            .Where(route => route.ClusterId == "api")
            .Select(route => route.RouteId)
            .Order(StringComparer.Ordinal);

        // cms, the media upload, media and webhooks also target the api cluster; what matters is that site and admin
        // do not.
        Assert.Equal(["api", "cms", "cms-media-upload", "media", "webhooks-square"], tokenBearingClusters);
    }

    [Theory]
    [InlineData("X-Original-For")]
    [InlineData("X-Original-Host")]
    [InlineData("X-Forwarded-Port")]
    [InlineData("X-Real-IP")]
    [InlineData("True-Client-IP")]
    [InlineData("CF-Connecting-IP")]
    [InlineData("X-Original-URL")]
    [InlineData("X-Rewrite-URL")]
    [InlineData("X-MS-CLIENT-PRINCIPAL")]
    [InlineData("baggage")]
    public async Task OtherSpoofableIdentityHeadersAreStripped(string header)
    {
        // Nothing downstream reads these today. They are removed so a later component that does — or a platform
        // that already does, as Container Apps does with X-MS-CLIENT-PRINCIPAL — cannot be fed by a client.
        var received = await Forward(request => request.Headers.TryAddWithoutValidation(header, "forged"));

        Assert.False(received.Has(header));
    }

    // There is deliberately no test here that an over-limit body is refused. YARP applies MaxRequestBodySize
    // through IHttpMaxRequestBodySizeFeature, which Kestrel implements and the in-memory test server does not: a
    // 256 KB + 1 body is forwarded with a 200 here. Asserting that would lock in the test server's behaviour as
    // though it were the gateway's. The value is asserted in GatewayRouteTableTests and the enforcement is checked
    // against real Kestrel under `aspire run` (ADR 0022 records the gap).
    [Fact]
    public async Task ARequestWithinTheWebhookSizeLimitIsForwarded()
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        using var body = new ByteArrayContent(new byte[64 * 1024]);

        var response = await client.PostAsync("/webhooks/square", body, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, destination.Requests);
    }

    [Fact]
    public async Task TheMediaRouteForwardsToTheApiCluster()
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        var response = await client.GetAsync("/media/an-image.jpg", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/media/an-image.jpg", (await Parse(response)).Path);
    }

    [Fact]
    public async Task TheWebhookRouteAcceptsOnlyPost()
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        // The route matches POST only, so a GET falls through to the site catch-all, whose cluster has no
        // destination yet. Either way it must not reach the API's webhook intake as a GET.
        var get = await client.GetAsync("/webhooks/square", TestContext.Current.CancellationToken);
        Assert.NotEqual(HttpStatusCode.OK, get.StatusCode);

        var post = await client.PostAsync("/webhooks/square", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
    }

    /// <summary>Sends a GET through the gateway's <c>/api</c> route and returns what the destination saw.</summary>
    private async Task<ReceivedRequest> Forward(Action<HttpRequestMessage>? configure = null)
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/site/health-info");
        configure?.Invoke(request);

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return await Parse(response);
    }

    private static async Task<ReceivedRequest> Parse(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<ReceivedRequest>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), Web)!;
}
