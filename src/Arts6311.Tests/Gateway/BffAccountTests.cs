using System.Net;
using System.Text.Json;
using Arts6311.Gateway.Edge;
using Arts6311.Gateway.Session;
using Arts6311.Tests.Browser;
using Arts6311.Tests.Gateway.Infrastructure;
using Arts6311.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// <c>/bff/forgot-password</c>, <c>/bff/reset-password</c> and <c>/bff/confirm-email</c> (auth.md, ADR 0028): what
/// the browser is told, and what it is never told.
/// </summary>
public sealed class BffAccountTests : IDisposable
{
    private const string LinkToken = "CfDJ8-a-reset-token-that-must-never-be-logged";
    private const string NewPhrase = "a-new-passphrase-that-must-never-be-logged";

    private readonly GatewayFactory gateway = new();

    [Fact]
    public async Task ForgotPasswordIs202WithNoBodyAndIsNeverCached()
    {
        using var browser = gateway.CreateBffClient();

        using var response = await ForgotAsync(browser, "ada@example.test");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task TheBrowserIsAnsweredBeforeTheApiIsAsked()
    {
        // The timing oracle, closed structurally (ADR 0028): the API is held and never answers, and the browser
        // still has its 202. Nothing that knows whether the address is registered ran before the response, so the
        // response's timing cannot depend on it.
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.Identity.HoldResetRequests = hold;
        using var browser = gateway.CreateBffClient();

        using var response = await ForgotAsync(browser, "ada@example.test").WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // And the request did go on to the API, which is still sitting on it.
        Assert.Equal("ada@example.test", await gateway.Identity.NextResetRequestAsync());
        Assert.False(hold.Task.IsCompleted);

        hold.SetResult();
    }

    [Fact]
    public async Task TheCooldownTreatsARegisteredAndAnUnknownAddressIdentically()
    {
        // The gateway never learns whether an address is registered — the API answers 202 either way, after the
        // browser has been answered — so the cooldown cannot treat the two differently. This pins what it does
        // instead: the first request per address goes on, a repeat inside the window is answered the same and
        // dropped, for both.
        using var browser = gateway.CreateBffClient();
        const string registered = "ada@example.test";
        const string unknown = "nobody@example.test";

        HttpResponseMessage[] answers =
        [
            await ForgotAsync(browser, registered),
            await ForgotAsync(browser, registered),
            await ForgotAsync(browser, unknown),
            await ForgotAsync(browser, unknown),
            await ForgotAsync(browser, "last@example.test"),
        ];

        // The dispatcher drains in order, so reaching the last address proves the repeats were never queued.
        Assert.Equal(registered, await gateway.Identity.NextResetRequestAsync());
        Assert.Equal(unknown, await gateway.Identity.NextResetRequestAsync());
        Assert.Equal("last@example.test", await gateway.Identity.NextResetRequestAsync());
        Assert.Equal(3, gateway.Identity.ResetRequests.Count);

        var first = await DescribeAsync(answers[0]);
        foreach (var answer in answers)
        {
            Assert.Equal(first, await DescribeAsync(answer));
            answer.Dispose();
        }
    }

    [Fact]
    public async Task TheCooldownCoversEverySpellingIdentityWouldMatch()
    {
        // Identity looks addresses up upper-cased, so these reach one account; varying the case must not reset the
        // cooldown, or one mailbox could still be flooded.
        using var browser = gateway.CreateBffClient();

        (await ForgotAsync(browser, "Ada@Example.test")).Dispose();
        (await ForgotAsync(browser, "ada@example.TEST")).Dispose();
        (await ForgotAsync(browser, "last@example.test")).Dispose();

        Assert.Equal("Ada@Example.test", await gateway.Identity.NextResetRequestAsync());
        Assert.Equal("last@example.test", await gateway.Identity.NextResetRequestAsync());
    }

    [Fact]
    public async Task TheCooldownEnds()
    {
        using var browser = gateway.CreateBffClient();

        (await ForgotAsync(browser, "ada@example.test")).Dispose();
        Assert.Equal("ada@example.test", await gateway.Identity.NextResetRequestAsync());

        gateway.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        (await ForgotAsync(browser, "ada@example.test")).Dispose();
        Assert.Equal("ada@example.test", await gateway.Identity.NextResetRequestAsync());
    }

    [Fact]
    public async Task TheStoreHoldsAHashNotTheAddress()
    {
        using var browser = gateway.CreateBffClient();

        (await ForgotAsync(browser, "ada@example.test")).Dispose();

        var key = Assert.Single(gateway.Sessions.Keys);
        Assert.StartsWith("6311:password-reset-cooldown:", key, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", key, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PasswordResetRequests.CooldownKey("ada@example.test"), key);
    }

    [Fact]
    public async Task AnUnreachableApiIsStill202AndLaterRequestsStillGoThrough()
    {
        // Nobody is waiting on the API's answer, so a failure is logged and dropped; the dispatcher must not stop.
        gateway.Identity.Unavailable = true;
        using var browser = gateway.CreateBffClient();

        using var whileDown = await ForgotAsync(browser, "ada@example.test");
        Assert.Equal(HttpStatusCode.Accepted, whileDown.StatusCode);
        Assert.Equal("ada@example.test", await gateway.Identity.NextResetRequestAsync());

        gateway.Identity.Unavailable = false;

        (await ForgotAsync(browser, "grace@example.test")).Dispose();
        Assert.Equal("grace@example.test", await gateway.Identity.NextResetRequestAsync());
    }

    [Fact]
    public async Task AFailedDispatchGivesTheCooldownBackSoTheRetryGoesThrough()
    {
        // Otherwise the person was told 202, got nothing, and every retry for five minutes would be dropped too.
        // Releasing it reveals nothing: the browser's answer is 202 whatever the cooldown holds.
        gateway.Identity.Unavailable = true;
        using var browser = gateway.CreateBffClient();

        (await ForgotAsync(browser, "ada@example.test")).Dispose();
        Assert.Equal("ada@example.test", await gateway.Identity.NextResetRequestAsync());

        // The dispatcher is sequential, so by the time this one reaches the API the failure above has been
        // handled, release included.
        (await ForgotAsync(browser, "sentinel@example.test")).Dispose();
        Assert.Equal("sentinel@example.test", await gateway.Identity.NextResetRequestAsync());

        gateway.Identity.Unavailable = false;

        (await ForgotAsync(browser, "ada@example.test")).Dispose();
        Assert.Equal("ada@example.test", await gateway.Identity.NextResetRequestAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AMissingAddressIs400(string? email)
    {
        using var browser = gateway.CreateBffClient();

        using var response = await browser.PostAsync("/bff/forgot-password", new { email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(GatewayProblem.Codes.ValidationFailed, (await ProblemAsync(response)).Code);
        Assert.Empty(gateway.Identity.ResetRequests);
    }

    [Fact]
    public async Task AnOversizedAddressIs400()
    {
        using var browser = gateway.CreateBffClient();

        using var response = await ForgotAsync(browser, new string('a', 250) + "@x.test");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnAddressThatIsNotValidUnicodeIsNeverAnOutage()
    {
        // A lone surrogate, written as the raw JSON escape a hostile client would send. string.Normalize would
        // throw on one, but it never gets that far: System.Text.Json refuses ill-formed UTF-16 while binding, and
        // outside Development that is a plain 400 (in Development, ThrowOnBadRequest makes every malformed /bff body
        // a 500 — the framework's choice, true of /bff/login too). The endpoint's ArgumentException catch is the
        // backstop if binding ever stops refusing it. Either way: never the 503 that means "the store is down".
        using var production = new GatewayFactory(environmentName: "Production");
        using var browser = production.CreateBffClient();
        await browser.EnsureAntiforgeryAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/forgot-password")
        {
            Content = new StringContent(
                "{\"email\":\"ada\\ud800@example.test\"}", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(AntiforgeryCookie.HeaderName, browser.RequestToken);

        using var response = await browser.Http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(production.Identity.ResetRequests);
    }

    [Theory]
    [InlineData("/bff/forgot-password")]
    [InlineData("/bff/reset-password")]
    [InlineData("/bff/confirm-email")]
    public async Task EachRouteRefusesARequestWithoutAntiforgery(string route)
    {
        using var browser = gateway.CreateBffClient();

        using var response = await browser.PostAsync(
            route,
            new { email = "ada@example.test", userId = "user-1", token = LinkToken, newPassword = NewPhrase },
            withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(GatewayProblem.Codes.AntiforgeryRequired, (await ProblemAsync(response)).Code);
        Assert.Empty(gateway.Identity.ResetRequests);
        Assert.Equal(0, gateway.Identity.ResetCompletions);
        Assert.Equal(0, gateway.Identity.Confirmations);
    }

    [Fact]
    public async Task TheThreeRoutesShareARateLimitThatSignInDoesNot()
    {
        using var limited = new GatewayFactory()
            .With("Gateway:RateLimits:bff-account:PermitLimit", "2")
            .With("Gateway:RateLimits:bff-account:Window", "00:05:00");
        using var browser = limited.CreateBffClient();

        using var first = await ForgotAsync(browser, "ada@example.test");
        using var second = await ConfirmAsync(browser);
        using var third = await ResetAsync(browser);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(0, limited.Identity.ResetCompletions);

        // A flood of reset requests must not spend the sign-in budget.
        using var login = await BffSessionTests.Login(browser);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task AResetEndsASecondLiveSessionAtItsNextRevalidation()
    {
        using var signedIn = gateway.CreateBffClient();
        using (var login = await BffSessionTests.Login(signedIn))
        {
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        // Another browser — the emailed link, opened anywhere — resets the password. The API rotates the stamp.
        using var resetter = gateway.CreateBffClient();
        using var reset = await ResetAsync(resetter);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using var beforeTheInterval = await signedIn.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, beforeTheInterval.StatusCode);

        gateway.Time.Advance(TimeSpan.FromMinutes(6));

        using var afterTheInterval = await signedIn.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.Unauthorized, afterTheInterval.StatusCode);
    }

    [Theory]
    [InlineData("auth.token_invalid")]
    [InlineData("validation.failed")]
    [InlineData("auth.some_future_code")]
    public async Task EveryUnusableLinkGetsOneAnswer(string apiCode)
    {
        // Whatever the API said was wrong with the link, the browser hears the same thing.
        gateway.Identity.LinkResult = new AccountLinkResult(apiCode, []);
        using var browser = gateway.CreateBffClient();

        using var reset = await ResetAsync(browser);
        using var confirm = await ConfirmAsync(browser);

        foreach (var response in new[] { reset, confirm })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var (code, title) = await ProblemAsync(response);
            Assert.Equal(GatewayProblem.Codes.TokenInvalid, code);
            Assert.Equal("This link has expired or has already been used. Request a new one.", title);
        }
    }

    [Fact]
    public async Task ARejectedPasswordSaysWhy()
    {
        // Reachable only with a valid token (the API checks it first), so the reasons are the link holder's.
        gateway.Identity.LinkResult = new AccountLinkResult(
            GatewayProblem.Codes.PasswordRejected, ["A staff password must be at least 14 characters."]);
        using var browser = gateway.CreateBffClient();

        using var response = await ResetAsync(browser);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(GatewayProblem.Codes.PasswordRejected, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "A staff password must be at least 14 characters.",
            body.RootElement.GetProperty("errors").GetProperty("newPassword")[0].GetString());
        Assert.True(body.RootElement.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task AConfirmationIs204()
    {
        using var browser = gateway.CreateBffClient();

        using var response = await ConfirmAsync(browser);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, gateway.Identity.Confirmations);
    }

    [Fact]
    public async Task AnUnreachableApiIs503ForAResetAndAConfirmation()
    {
        // "No answer" is not "this link has expired": telling someone to request a new link during an outage
        // would send them round the loop for nothing.
        gateway.Identity.Unavailable = true;
        using var browser = gateway.CreateBffClient();

        using var reset = await ResetAsync(browser);
        using var confirm = await ConfirmAsync(browser);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reset.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, confirm.StatusCode);
        Assert.Equal(GatewayProblem.Codes.IdentityUnavailable, (await ProblemAsync(reset)).Code);
    }

    [Theory]
    [InlineData("", LinkToken)]
    [InlineData("user-1", "")]
    [InlineData("user-1", null)]
    public async Task AnImplausibleLinkIs400WithoutAskingTheApi(string userId, string? token)
    {
        using var browser = gateway.CreateBffClient();

        using var reset = await browser.PostAsync("/bff/reset-password", new { userId, token, newPassword = NewPhrase });
        using var confirm = await browser.PostAsync("/bff/confirm-email", new { userId, token });

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal(0, gateway.Identity.ResetCompletions);
        Assert.Equal(0, gateway.Identity.Confirmations);
    }

    [Fact]
    public async Task AnOversizedTokenIs400WithoutAskingTheApi()
    {
        using var browser = gateway.CreateBffClient();

        using var response = await browser.PostAsync(
            "/bff/reset-password", new { userId = "user-1", token = new string('a', 2049), newPassword = NewPhrase });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, gateway.Identity.ResetCompletions);
    }

    [Fact]
    public async Task NoTokenPasswordOrAddressIsEverLogged()
    {
        var sink = new CapturingLoggerProvider();
        using var logged = gateway.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(sink);
            logging.SetMinimumLevel(LogLevel.Trace);
        }));
        using var browser = new BffClient(logged.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        }));

        (await ForgotAsync(browser, "ada.lovelace@example.test")).Dispose();
        Assert.Equal("ada.lovelace@example.test", await gateway.Identity.NextResetRequestAsync());
        (await ResetAsync(browser)).Dispose();
        (await ConfirmAsync(browser)).Dispose();

        // The failure paths too, since an error is where a value is most likely to be quoted.
        gateway.Identity.Unavailable = true;
        (await ForgotAsync(browser, "grace.hopper@example.test")).Dispose();
        await gateway.Identity.NextResetRequestAsync();
        (await ResetAsync(browser)).Dispose();

        var text = sink.Text();
        Assert.NotEmpty(text);
        Assert.DoesNotContain(LinkToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPhrase, text, StringComparison.Ordinal);
        Assert.DoesNotContain("lovelace", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hopper", text, StringComparison.OrdinalIgnoreCase);
    }

    private static Task<HttpResponseMessage> ForgotAsync(BffClient browser, string email) =>
        browser.PostAsync("/bff/forgot-password", new { email });

    private static Task<HttpResponseMessage> ResetAsync(BffClient browser) =>
        browser.PostAsync("/bff/reset-password", new { userId = "user-1", token = LinkToken, newPassword = NewPhrase });

    private static Task<HttpResponseMessage> ConfirmAsync(BffClient browser) =>
        browser.PostAsync("/bff/confirm-email", new { userId = "user-1", token = LinkToken });

    /// <summary>Everything a browser could compare between two answers: status, header names and body.</summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content.Headers)
            .Select(header => header.Key)
            .Order(StringComparer.OrdinalIgnoreCase);

        return $"{(int)response.StatusCode} [{string.Join(',', headers)}] {await response.Content.ReadAsStringAsync(Ct)}";
    }

    private static async Task<(string? Code, string? Title)> ProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = document.RootElement;

        return (root.GetProperty("code").GetString(), root.GetProperty("title").GetString());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => gateway.Dispose();
}
