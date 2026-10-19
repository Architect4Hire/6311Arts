using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Gateway.Edge;
using Arts6311.Tests.Gateway.Infrastructure;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// Antiforgery at the edge (<c>auth.md</c> "CSRF"): every unsafe request needs a token, and the exceptions are
/// listed explicitly — which today means exactly <c>/webhooks/square</c>.
/// </summary>
public sealed class AntiforgeryTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private StubDestination destination = null!;

    public async ValueTask InitializeAsync() => destination = await StubDestination.StartAsync();

    public async ValueTask DisposeAsync() => await destination.DisposeAsync();

    [Fact]
    public async Task TheTokenArrivesAsTwoCookiesAndNotInABody()
    {
        // Never in a body: the gateway compresses responses, and a secret in a compressed body beside
        // attacker-controlled content is a BREACH oracle (the obligation ADR 0022 left here).
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        using var response = await client.GetAsync("/bff/antiforgery", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength ?? 0);

        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();

        var secret = Assert.Single(
            cookies, cookie => cookie.StartsWith(AntiforgeryCookie.CookieTokenName + "=", StringComparison.Ordinal));
        var readable = Assert.Single(
            cookies, cookie => cookie.StartsWith(AntiforgeryCookie.RequestTokenName + "=", StringComparison.Ordinal));

        // The secret half is unreadable to script; the half script must read is not.
        Assert.Contains("httponly", secret, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("httponly", readable, StringComparison.OrdinalIgnoreCase);

        foreach (var cookie in cookies)
        {
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AnUnsafeRequestWithoutATokenIs400()
    {
        using var gateway = new GatewayFactory(destination);
        using var browser = gateway.CreateBffClient();

        using var response = await browser.PostAsync(
            "/bff/login", new { email = "ada@example.test", password = "a-long-enough-password" },
            withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("auth.antiforgery_required", body.GetProperty("code").GetString());

        // Refused at the edge: no credential check was even attempted.
        Assert.Equal(0, gateway.Identity.CredentialCalls);
    }

    [Fact]
    public async Task AProxiedWriteWithoutATokenNeverReachesTheApi()
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        using var response = await client.PostAsync(
            "/api/v1/forms/contact", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, destination.Requests);
    }

    [Fact]
    public async Task TheSquareWebhookIsExempt()
    {
        // Square is not a browser and has no token to carry. The webhook is verified by signature instead
        // (outbox.md), and it is the only exemption.
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        using var response = await client.PostAsync(
            "/webhooks/square", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, destination.Requests);
    }

    [Theory]
    [InlineData("/webhooks/square/extra")]
    [InlineData("/webhooks/squarex")]
    public async Task OnlyTheWebhookItselfIsExempt(string path)
    {
        // A prefix match would exempt anything under /webhooks/square — and those paths are not routes, so they
        // fall through to the site catch-all and would arrive at the SSR app as unchecked writes.
        using var gateway = new GatewayFactory(destination).With("Services:site:http:0", destination.Address);
        using var client = gateway.CreateJsonClient();

        using var response = await client.PostAsync(path, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, destination.Requests);
    }

    [Fact]
    public async Task TheWebhookExemptionIsPostOnly()
    {
        // The route itself is POST-only, so anything else here is somebody probing. It gets no free pass.
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        using var request = new HttpRequestMessage(HttpMethod.Put, "/webhooks/square");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AFormPostWithoutTheHeaderIsRefusedWithoutReadingTheBody()
    {
        // Handed to the antiforgery service, a form-encoded body would be read looking for a field token: an
        // attacker-controlled body buffered at the edge, and a second way to present a token that nothing in
        // this system uses. The header is the only way in.
        using var gateway = new GatewayFactory(destination);
        using var browser = gateway.CreateBffClient();

        // A valid pair is fetched first, so what is under test is the content type and not a missing token.
        await browser.EnsureAntiforgeryAsync();

        using var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("__RequestVerificationToken", "anything"),
        ]);

        using var response = await browser.Http.PostAsync(
            "/bff/login", form, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("auth.antiforgery_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ARoleChangeDoesNotInvalidateTheTokenTheBrowserHolds()
    {
        // Written to find out, not to confirm: the token is bound to the signed-in identity, and if that binding
        // covered the role claims then every role change would refuse the next write with a 400 that no client
        // could anticipate. The session id is what it is bound to, so it does not.
        using var gateway = new GatewayFactory(destination);
        using var browser = gateway.CreateBffClient();

        using var login = await BffSessionTests.Login(browser);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        gateway.Identity.RolesOnRevalidation = ["Editor", "Admin"];
        gateway.Time.Advance(TimeSpan.FromMinutes(6));

        using var afterTheRoleChange = await browser.PostAsync("/bff/logout");

        Assert.Equal(HttpStatusCode.NoContent, afterTheRoleChange.StatusCode);
    }

    [Fact]
    public async Task ASafeRequestNeedsNoToken()
    {
        using var gateway = new GatewayFactory(destination);
        using var client = gateway.CreateJsonClient();

        using var response = await client.GetAsync(
            "/api/v1/site/health-info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ATokenMintedBeforeSignInDoesNotWorkAfterwards()
    {
        // Antiforgery tokens are bound to the signed-in identity, which is why /bff/login re-issues the pair and
        // why validation runs after authentication (ADR 0024). Without both, the first write after signing in
        // would be refused with a 400 that looks exactly like a CSRF bug.
        using var gateway = new GatewayFactory(destination);
        using var browser = gateway.CreateBffClient();

        await browser.EnsureAntiforgeryAsync();
        var anonymousToken = AntiforgeryTokenOf(browser);

        using var login = await BffSessionTests.Login(browser);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // The stale token, presented by hand.
        using var stale = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        stale.Headers.TryAddWithoutValidation(AntiforgeryCookie.HeaderName, anonymousToken);

        using var refused = await browser.Http.SendAsync(stale, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // The one the sign-in handed back does work.
        using var accepted = await browser.PostAsync("/bff/logout");
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    private static string AntiforgeryTokenOf(BffClient browser) => browser.RequestToken!;
}
