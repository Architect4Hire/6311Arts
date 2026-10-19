using System.Net;
using Arts6311.Tests.Gateway.Infrastructure;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The two clocks on a session (<c>auth.md</c> "Lifetimes"): 30 minutes idle and 8 hours absolute for staff,
/// the 1.10 defaults for everyone else. These are the compensating controls for having no second factor
/// (ADR 0018), so each one is asserted on its own.
/// </summary>
public sealed class SessionLifetimeTests : IDisposable
{
    private readonly GatewayFactory gateway = new();

    [Fact]
    public async Task AStaffSessionSurvivesUpToTheIdleLimit()
    {
        using var browser = await SignedInAsync();

        gateway.Time.Advance(TimeSpan.FromMinutes(29));

        using var response = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AStaffSessionEndsAfterThirtyIdleMinutes()
    {
        using var browser = await SignedInAsync();

        gateway.Time.Advance(TimeSpan.FromMinutes(31));

        using var response = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Gone, not merely refused: the store's own expiry is the idle window, so nothing has to sweep it.
        Assert.Empty(gateway.Sessions.Keys);
    }

    [Fact]
    public async Task EachRequestRestartsTheIdleWindow()
    {
        using var browser = await SignedInAsync();

        // Four twenty-minute gaps is eighty minutes, well past the idle limit, and none of them reaches it.
        for (var step = 0; step < 4; step++)
        {
            gateway.Time.Advance(TimeSpan.FromMinutes(20));

            using var response = await browser.GetAsync("/bff/session");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task AStaffSessionEndsAfterEightHoursHoweverBusyItIs()
    {
        using var browser = await SignedInAsync();

        // Kept alive by a request every twenty minutes, so only the absolute limit can end it.
        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromHours(7.5); elapsed += TimeSpan.FromMinutes(20))
        {
            gateway.Time.Advance(TimeSpan.FromMinutes(20));

            using var alive = await browser.GetAsync("/bff/session");
            Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
        }

        gateway.Time.Advance(TimeSpan.FromMinutes(40));

        using var response = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(gateway.Sessions.Keys);
    }

    [Fact]
    public async Task AMemberSessionUsesTheLongerDefaults()
    {
        // The staff limits are the exception, not the rule: a member idle for two hours is still signed in,
        // where an editor would have been signed out ninety minutes ago.
        gateway.Identity.User = gateway.Identity.User with { Roles = ["Member"] };

        using var browser = await SignedInAsync();

        gateway.Time.Advance(TimeSpan.FromHours(2));

        using var response = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AMemberSessionStillEndsAtItsOwnIdleLimit()
    {
        gateway.Identity.User = gateway.Identity.User with { Roles = ["Member"] };

        using var browser = await SignedInAsync();

        gateway.Time.Advance(TimeSpan.FromDays(8));

        using var response = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AMemberWhoIsAlsoStaffGetsTheStaffLimits()
    {
        // The shorter window wins. Reading it the other way would let an editor keep an eight-day session by
        // also holding the Member role.
        gateway.Identity.User = gateway.Identity.User with { Roles = ["Member", "Editor"] };

        using var browser = await SignedInAsync();

        gateway.Time.Advance(TimeSpan.FromMinutes(31));

        using var response = await browser.GetAsync("/bff/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BeingPromotedToStaffBringsTheAbsoluteLimitForward()
    {
        // Someone made an editor mid-session must come under the 8-hour staff cap, not keep the 30-day deadline
        // they signed in with as a member. That cap is a compensating control for having no second factor
        // (ADR 0018), and a session that kept the longer one would quietly be outside it.
        gateway.Identity.User = gateway.Identity.User with { Roles = ["Member"] };

        using var browser = await SignedInAsync();

        gateway.Identity.RolesOnRevalidation = ["Editor"];
        gateway.Time.Advance(TimeSpan.FromMinutes(6));

        using var promoted = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);

        // Kept busy, so only an absolute limit can end it. Eight hours and twenty minutes from sign-in.
        for (var step = 0; step < 25; step++)
        {
            gateway.Time.Advance(TimeSpan.FromMinutes(20));
            using var request = await browser.GetAsync("/bff/session");

            if (request.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Somewhere in the eighth hour, as the staff cap requires.
                Assert.True(gateway.Time.GetUtcNow().Hour >= 8 - 1);
                return;
            }
        }

        Assert.Fail("The session outlived the staff absolute limit it came under when the roles changed.");
    }

    [Fact]
    public async Task LosingAStaffRoleDoesNotExtendTheSession()
    {
        // The deadline only ever moves forward. Otherwise an editor could buy themselves a thirty-day session by
        // having the Editor role taken away an hour in.
        using var browser = await SignedInAsync();

        gateway.Identity.RolesOnRevalidation = ["Member"];
        gateway.Time.Advance(TimeSpan.FromMinutes(6));

        using var demoted = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, demoted.StatusCode);

        for (var step = 0; step < 25; step++)
        {
            gateway.Time.Advance(TimeSpan.FromMinutes(20));
            using var request = await browser.GetAsync("/bff/session");

            if (request.StatusCode == HttpStatusCode.Unauthorized)
            {
                return;
            }
        }

        Assert.Fail("The session outlived the absolute limit it was created with.");
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
