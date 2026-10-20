using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Gateway.Session;
using Arts6311.Tests.Browser;
using Arts6311.Tests.Gateway.Infrastructure;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// Signing in, signing out, and what <c>/bff/session</c> says in between (<c>auth.md</c> "Browser session").
/// </summary>
public sealed class BffSessionTests : IDisposable
{
    private readonly GatewayFactory gateway = new();

    [Fact]
    public async Task ASignInCreatesATicketAndSetsTheCookie()
    {
        using var browser = gateway.CreateBffClient();

        var response = await Login(browser);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-6311.session=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);

        // A browser-session cookie: nothing on disk, and the server-side ticket is the authority (ADR 0024).
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);

        Assert.Single(gateway.Sessions.Keys);
    }

    [Fact]
    public async Task TheStoreIsKeyedOnTheHashOfTheCookieAndNotOnTheCookie()
    {
        // Whoever reads the ticket store cannot turn what they see back into a usable cookie (ADR 0024).
        using var browser = gateway.CreateBffClient();
        using var response = await Login(browser);

        var ticketKey = browser.SessionCookie!;
        var storedKey = Assert.Single(gateway.Sessions.Keys);

        Assert.DoesNotContain(ticketKey, storedKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheResponseGivesTheBrowserNoTokenAndNoIdentifiers()
    {
        using var browser = gateway.CreateBffClient();
        using var response = await Login(browser);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // auth.md: "No tokens to the browser, ever." Nor the user id, nor the security stamp.
        Assert.DoesNotContain("Bearer", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(gateway.Identity.User.UserId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(gateway.Identity.User.SecurityStamp, body, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Authorization"));
    }

    [Theory]
    [InlineData("auth.credentials_invalid")]
    [InlineData("auth.locked_out")]
    [InlineData("auth.email_unconfirmed")]
    public async Task EveryKindOfFailureLooksIdentical(string apiFailureCode)
    {
        // The API distinguishes these for the gateway's log and its audit. A browser that could tell them apart
        // could enumerate accounts, which is why 1.10 collapses them (ADR 0023).
        gateway.Identity.FailureCode = apiFailureCode;

        using var browser = gateway.CreateBffClient();
        using var response = await Login(browser);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal("auth.credentials_invalid", body.GetProperty("code").GetString());
        Assert.Equal(
            "Check your email address and password, then try again.", body.GetProperty("title").GetString());

        // Nothing that distinguishes one cause from another survives. The one answer is the invalid-credentials
        // code, whichever of the three the API actually reported; the distinction lives in the gateway's log and
        // the API's audit row.
        Assert.DoesNotContain("locked", body.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirm", body.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(gateway.Sessions.Keys);
    }

    [Fact]
    public async Task TheSessionEndpointReturnsADisplayNameAndRolesAndNothingElse()
    {
        using var browser = gateway.CreateBffClient();
        using var login = await Login(browser);

        using var response = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["displayName", "roles"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        Assert.Equal("Ada Editor", body.GetProperty("displayName").GetString());
        Assert.Equal(["Editor"], body.GetProperty("roles").EnumerateArray().Select(role => role.GetString()));
    }

    [Fact]
    public async Task TheSessionEndpointIs401WithoutASession()
    {
        using var browser = gateway.CreateBffClient();

        using var response = await browser.GetAsync("/bff/session");

        // 401 rather than a redirect, even for a browser: a client asking "am I signed in?" wants the answer.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/bff/session")]
    [InlineData("/bff/antiforgery")]
    public async Task NoBffResponseIsCacheable(string path)
    {
        // Every answer here is personal — a display name, roles, a Set-Cookie — and gateway.md forbids caching a
        // personalized response. Vary: Cookie because the session cookie is the only thing it depends on.
        using var browser = gateway.CreateBffClient();
        using var login = await Login(browser);

        using var response = await browser.GetAsync(path);

        // Antiforgery adds its own no-cache on the endpoint that issues tokens, so this asserts the property
        // rather than the exact string.
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("Cookie", string.Join(',', response.Headers.Vary));
    }

    [Fact]
    public async Task LogoutDeletesTheTicketAndClearsTheCookie()
    {
        using var browser = gateway.CreateBffClient();
        using var login = await Login(browser);

        using var response = await browser.PostAsync("/bff/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(gateway.Sessions.Keys);

        using var after = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task ACopiedCookieIsDeadAfterLogout()
    {
        // The point of a server-side ticket: a cookie somebody copied off the wire or out of a browser stops
        // working the moment the person signs out, with nothing to wait for (auth.md).
        using var browser = gateway.CreateBffClient();
        using var login = await Login(browser);

        var copied = browser.SessionCookie!;

        using var thief = gateway.CreateJsonClient();
        thief.DefaultRequestHeaders.Add("Cookie", $"__Host-6311.session={copied}");

        using var beforeLogout = await thief.GetAsync("/bff/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, beforeLogout.StatusCode);

        using var logout = await browser.PostAsync("/bff/logout");

        using var afterLogout = await thief.GetAsync("/bff/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task LogoutWithoutASessionIsNotAnError()
    {
        // Answering 401 to "end my session" leaves a browser holding a cookie it cannot get rid of.
        using var browser = gateway.CreateBffClient();

        using var response = await browser.PostAsync("/bff/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task SigningInAgainReplacesTheTicketRatherThanAddingOne()
    {
        using var browser = gateway.CreateBffClient();
        using var first = await Login(browser);
        var firstCookie = browser.SessionCookie;

        browser.ForgetAntiforgeryToken();
        using var second = await Login(browser);

        Assert.NotEqual(firstCookie, browser.SessionCookie);
        Assert.Single(gateway.Sessions.Keys);
    }

    [Fact]
    public async Task SignInIsRateLimited()
    {
        // auth.md: the bff-login limit is never relaxed without an ADR. Ten in five minutes, per client.
        gateway.Identity.FailureCode = "auth.credentials_invalid";

        using var browser = gateway.CreateBffClient();
        await browser.EnsureAntiforgeryAsync();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var allowed = await Login(browser);
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var refused = await Login(browser);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task AnUnreachableIdentityApiIs503AndNotARefusal()
    {
        // "We could not ask" is not "your password is wrong": a 401 here would have people resetting passwords
        // that are perfectly good.
        gateway.Identity.Unavailable = true;

        using var browser = gateway.CreateBffClient();
        using var response = await Login(browser);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("auth.identity_unavailable", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("", "secret-enough")]
    [InlineData("someone@example.test", "")]
    public async Task AnIncompleteBodyIsRefusedBeforeTheApiIsAsked(string email, string password)
    {
        using var browser = gateway.CreateBffClient();

        using var response = await browser.PostAsync("/bff/login", new { email, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, gateway.Identity.CredentialCalls);
    }

    internal static Task<HttpResponseMessage> Login(BffClient browser) =>
        browser.PostAsync("/bff/login", new { email = "ada@example.test", password = "a-long-enough-password" });

    public void Dispose() => gateway.Dispose();
}
