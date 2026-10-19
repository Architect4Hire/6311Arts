using System.Net;
using System.Text.Json;
using Arts6311.Tests.Gateway.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The coarse route gate (<c>gateway.md</c>): <c>/admin/**</c> and <c>/api/v1/cms/**</c> need a
/// <c>CmsEditor</c> session, and a session is what turns a proxied request into one carrying an internal token.
/// </summary>
public sealed class SessionRouteAuthorizationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string CmsPath = "/api/v1/cms/pages";

    private StubDestination destination = null!;

    public async ValueTask InitializeAsync() => destination = await StubDestination.StartAsync();

    public async ValueTask DisposeAsync() => await destination.DisposeAsync();

    [Fact]
    public async Task ABrowserNavigationToAdminWithoutASessionIsSentToLogin()
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateClient(new WebApplicationFactoryClientOptions
        {
            // Not followed, so the redirect itself can be read. Following it is also how the loop below was found.
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/classes");
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/signin?returnUrl=%2Fadmin%2Fclasses", response.Headers.Location?.OriginalString);
        Assert.Equal(0, destination.Requests);
    }

    [Fact]
    public async Task TheLoginPageIsNotItselfBehindTheGate()
    {
        // Found by following the redirect: with the login page inside /admin/**, the browser is sent to a page
        // that redirects it to the same place, forever — and its scripts would be refused too, so the form could
        // not render even if the document were let through (ADR 0024).
        // The sign-in page is the site's, so the site cluster is the one pointed at the stub here.
        using var gateway = new GatewayFactory(destination).With("Services:site:http:0", destination.Address);
        using var browser = gateway.CreateBffClient();

        using var response = await browser.GetAsync("/signin", accept: "text/html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, destination.Requests);
    }

    [Fact]
    public async Task ACmsApiCallWithoutASessionIs401AndNeverReachesTheApi()
    {
        using var gateway = new GatewayFactory(destination);
        using var browser = gateway.CreateBffClient();

        using var response = await browser.GetAsync(CmsPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The refusal is the edge's. A 401 from the API would mean the request had been forwarded.
        Assert.Equal(0, destination.Requests);
    }

    [Fact]
    public async Task AMemberSessionIsForbiddenFromTheCmsSurface()
    {
        using var gateway = new GatewayFactory(destination);
        gateway.Identity.User = gateway.Identity.User with { Roles = ["Member"] };

        using var browser = await SignedInAsync(gateway);

        using var response = await browser.GetAsync(CmsPath);

        // 403, not a redirect: signing in again would not help, and saying so would be misleading.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, destination.Requests);
    }

    [Fact]
    public async Task AnEditorSessionReachesTheCmsSurfaceCarryingAUserToken()
    {
        using var gateway = new GatewayFactory(destination);
        using var browser = await SignedInAsync(gateway);

        using var response = await browser.GetAsync(CmsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var received = await Parse(response);
        var token = ReadToken(received);

        Assert.Equal("user", token.GetClaim("token_use").Value);
        Assert.Equal("user-1", token.Subject);
        Assert.Equal(["Editor"], token.Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value));

        // The session id, not the cookie's value: the thing that would resume the session never leaves the
        // gateway and its ticket store (ADR 0024).
        var sessionId = token.GetClaim("sid").Value;
        Assert.NotEqual(browser.SessionCookie, sessionId);
    }

    [Fact]
    public async Task TheSessionCookieIsNeverForwarded()
    {
        // The API takes no cookies at all (ADR 0002). The gateway reads the session and sends a token instead.
        using var gateway = new GatewayFactory(destination);
        using var browser = await SignedInAsync(gateway);

        using var response = await browser.GetAsync(CmsPath);
        var received = await Parse(response);

        Assert.False(received.Has("Cookie"));
    }

    [Fact]
    public async Task AnAnonymousSiteReadIsStillProxiedWithoutAToken()
    {
        // Secure by default is the API's job, not the edge's: the public site's reads must keep working for
        // somebody with no session, and must arrive with no credential (ADR 0021).
        using var gateway = new GatewayFactory(destination);
        using var browser = gateway.CreateBffClient();

        using var response = await browser.GetAsync("/api/v1/site/classes");
        var received = await Parse(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(received.Has("Authorization"));
    }

    [Fact]
    public async Task ASignedInSiteReadCarriesTheToken()
    {
        // The same route, now with a session behind it: the API needs to know who is asking for a member read.
        using var gateway = new GatewayFactory(destination);
        using var browser = await SignedInAsync(gateway);

        using var response = await browser.GetAsync("/api/v1/site/classes");
        var received = await Parse(response);

        Assert.StartsWith("Bearer ", received.Header("Authorization")!, StringComparison.Ordinal);
    }

    private static async Task<ReceivedRequest> Parse(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<ReceivedRequest>(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), Web)!;

    private static JsonWebToken ReadToken(ReceivedRequest received) =>
        new(received.Header("Authorization")!["Bearer ".Length..]);

    private static async Task<BffClient> SignedInAsync(GatewayFactory gateway)
    {
        var browser = gateway.CreateBffClient();
        using var login = await BffSessionTests.Login(browser);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return browser;
    }
}
