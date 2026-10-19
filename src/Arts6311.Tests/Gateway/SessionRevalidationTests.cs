using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Tests.Gateway.Infrastructure;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// Periodic security-stamp revalidation (<c>auth.md</c> "Revalidation"), and "log out everywhere" on top of it.
/// This is the whole revocation story: a live session is only ever one interval away from noticing.
/// </summary>
public sealed class SessionRevalidationTests : IDisposable
{
    private readonly GatewayFactory gateway = new();

    [Fact]
    public async Task TheStampIsNotCheckedOnEveryRequest()
    {
        // Revalidating per request would put an internal call in front of every page load. The interval is the
        // trade, and it is the window in which a revocation has not yet landed.
        using var browser = await SignedInAsync();

        for (var request = 0; request < 3; request++)
        {
            using var response = await browser.GetAsync("/bff/session");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(0, gateway.Identity.ValidateCalls);
    }

    [Fact]
    public async Task ARotatedStampEndsTheSessionAtTheNextRevalidation()
    {
        using var browser = await SignedInAsync();

        // What a password reset does on the API's side.
        gateway.Identity.CurrentStamp = "stamp-rotated";

        gateway.Time.Advance(TimeSpan.FromMinutes(1));
        using var beforeTheInterval = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, beforeTheInterval.StatusCode);

        gateway.Time.Advance(TimeSpan.FromMinutes(5));
        using var afterTheInterval = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.Unauthorized, afterTheInterval.StatusCode);
        Assert.Empty(gateway.Sessions.Keys);
    }

    [Fact]
    public async Task ARoleChangeReachesALiveSession()
    {
        using var browser = await SignedInAsync();

        gateway.Identity.RolesOnRevalidation = ["Editor", "Admin"];
        gateway.Time.Advance(TimeSpan.FromMinutes(6));

        using var response = await browser.GetAsync("/bff/session");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Admin", "Editor"],
            body.GetProperty("roles").EnumerateArray().Select(role => role.GetString()!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnUnreachableApiRefusesTheRequestWithoutEndingTheSession()
    {
        // Failing closed is right — we cannot say the session is still good — but deleting the ticket would turn
        // an API restart into signing everybody out (ADR 0024).
        using var browser = await SignedInAsync();

        gateway.Identity.Unavailable = true;
        gateway.Time.Advance(TimeSpan.FromMinutes(6));

        using var whileDown = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.Unauthorized, whileDown.StatusCode);
        Assert.Single(gateway.Sessions.Keys);

        gateway.Identity.Unavailable = false;

        using var afterRecovery = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, afterRecovery.StatusCode);
    }

    [Fact]
    public async Task LogoutEverywhereEndsThisSessionAndTheOthersAtTheirNextRevalidation()
    {
        using var first = await SignedInAsync();
        using var second = await SignedInAsync();

        Assert.Equal(2, gateway.Sessions.Keys.Count);

        using var response = await first.PostAsync("/bff/logout-everywhere");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, gateway.Identity.RevokeCalls);
        Assert.Equal("user-1", gateway.Identity.RevokedUserId);

        // The session that asked is gone at once.
        using var afterFirst = await first.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.Unauthorized, afterFirst.StatusCode);

        // The other one lives until its next revalidation, and no longer.
        using var secondBefore = await second.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, secondBefore.StatusCode);

        gateway.Time.Advance(TimeSpan.FromMinutes(6));

        using var secondAfter = await second.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.Unauthorized, secondAfter.StatusCode);
        Assert.Empty(gateway.Sessions.Keys);
    }

    [Fact]
    public async Task ARevalidationWriteCannotBringBackALoggedOutTicket()
    {
        // The write that follows a revalidation must not recreate a ticket a logout deleted while the API was
        // being asked. A plain SET would, and a copied cookie would come back to life for a full idle window.
        using var browser = await SignedInAsync();
        var storedKey = Assert.Single(gateway.Sessions.Keys);

        gateway.Time.Advance(TimeSpan.FromMinutes(6));

        // Stands in for the logout that lands mid-flight: the ticket is gone by the time the write happens.
        gateway.Identity.BeforeValidate = () => gateway.Sessions.Forget(storedKey);

        using var response = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(gateway.Sessions.Keys);
    }

    [Fact]
    public async Task LogoutEverywhereNeedsASession()
    {
        using var browser = gateway.CreateBffClient();

        using var response = await browser.PostAsync("/bff/logout-everywhere");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, gateway.Identity.RevokeCalls);
    }

    [Fact]
    public async Task AFailedRevocationLeavesThisSessionAlone()
    {
        // The promise is "every session ends". Ending only the one that asked, while the others live on, is
        // worse than refusing, because the person would believe the opposite.
        using var browser = await SignedInAsync();

        gateway.Identity.Unavailable = true;

        using var response = await browser.PostAsync("/bff/logout-everywhere");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        gateway.Identity.Unavailable = false;

        using var after = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    private async Task<BffClient> SignedInAsync()
    {
        var browser = gateway.CreateBffClient();
        using var login = await BffSessionTests.Login(browser);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        return browser;
    }

    public void Dispose() => gateway.Dispose();
}
