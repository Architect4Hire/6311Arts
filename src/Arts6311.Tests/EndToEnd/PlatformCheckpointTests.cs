using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Identity;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Tests.Browser;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.EndToEnd;

/// <summary>
/// The platform checkpoint (prompt 1.12): the AppHost from empty volumes, then the flows Phase 1 built, each through
/// the real hosts — the browser's only way in is the gateway.
/// </summary>
/// <remarks>
/// <para>
/// No per-test timeout: every wait is bounded already — startup by <see cref="PlatformFixture.StartupTimeout"/>,
/// each poll by its own deadline — and CI bounds the job.
/// </para>
/// <para>
/// Sign-ins are kept to a handful: <c>/bff/login</c> is rate limited per client IP (gateway.md), and every request
/// here comes from the same loopback address.
/// </para>
/// </remarks>
[Trait("Category", "E2E")]
public sealed class PlatformCheckpointTests(PlatformFixture platform, ITestOutputHelper output)
    : IClassFixture<PlatformFixture>
{
    /// <summary>The gateway endpoint a browser uses: HTTPS, because its cookies are Secure.</summary>
    private const string GatewayEndpoint = "https";

    private const string SessionCookieName = "__Host-6311.session";

    private static readonly string[] InternalPaths =
    [
        "/api/v1/internal/sessions/credentials",
        "/api/v1/internal/sessions/validate",
        "/api/v1/internal/sessions/revoke",
        "/api/v1/internal/identity/password-reset",
        "/api/v1/internal/identity/password-reset/complete",
        "/api/v1/internal/identity/email-confirmation",
        "/api/v1/internal/identity/email-confirmation/complete",
        "/api/v1/internal/anything-else",
    ];

    /// <summary>
    /// How long a CMS change may take to reach the site: the Worker's poll (<c>OutboxPolicy.PollingInterval</c>, five
    /// seconds), the bus, and a render, with room for a slow CI machine. Far inside the five-minute lifetime the bug
    /// produced.
    /// </summary>
    private static readonly TimeSpan PublishToPageBudget = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryResourceIsUpAndTheMigrationsFinishedCleanly()
    {
        var rows = await platform.SnapshotAsync(Ct);

        // The health table the checkpoint reports.
        output.WriteLine("| Resource | State | Health | Exit |");
        output.WriteLine("|---|---|---|---|");
        foreach (var row in rows)
        {
            var exit = row.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            output.WriteLine($"| {row.Name} | {row.State} | {row.Health} | {exit} |");
        }

        var migrations = Assert.Single(rows, row => row.Name == "migrations");
        Assert.Equal(0, migrations.ExitCode);

        foreach (var name in new[] { "sql", "cache", "storage", "mail", "api", "worker", "site", "admin", "gateway" })
        {
            var row = Assert.Single(rows, candidate => candidate.Name == name);
            Assert.Equal("Running", row.State);
            Assert.NotEqual("Unhealthy", row.Health);
        }
    }

    [Fact]
    public async Task TheGatewayReportsHealthy()
    {
        using var client = new HttpClient(DevCertificateHandler.Create())
        {
            BaseAddress = platform.App.GetEndpoint("gateway", GatewayEndpoint),
        };

        var response = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InternalRoutesAre404FromABrowserSignedInOrNot()
    {
        using var browser = Browser(out _);

        foreach (var path in InternalPaths)
        {
            var anonymous = await browser.PostAsync(path, new { userId = "x" });
            Assert.True(anonymous.StatusCode == HttpStatusCode.NotFound, $"{path} anonymous: {anonymous.StatusCode}");
        }

        // A signed-in staff session does not open them either: the lock is the path, not the identity.
        await SignInAsync(browser);
        browser.ForgetAntiforgeryToken();

        foreach (var path in InternalPaths)
        {
            var signedIn = await browser.PostAsync(path, new { userId = "x" });
            Assert.True(signedIn.StatusCode == HttpStatusCode.NotFound, $"{path} signed in: {signedIn.StatusCode}");
        }
    }

    [Fact]
    public async Task StaffSignInThroughTheBffAndTheSessionCarriesTheirRoles()
    {
        using var browser = Browser(out var jar);

        var signIn = await SignInAsync(browser);

        Assert.Contains("Admin", signIn.Roles);
        Assert.NotNull(browser.SessionCookie);

        // The attributes the gateway really sent over TLS (auth.md): __Host- demands Secure, Path=/ and no Domain.
        var setCookie = jar.LastSetCookie(SessionCookieName);
        Assert.NotNull(setCookie);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", setCookie, StringComparison.OrdinalIgnoreCase);

        var session = await browser.GetAsync("/bff/session", "application/json");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var body = await session.Content.ReadFromJsonAsync<SessionBody>(Ct);
        Assert.Contains("Admin", body!.Roles);

        // auth.md: the browser gets a display name and roles, never a token or an id.
        var raw = await session.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(PlatformFixture.AdminEmail, raw, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The walk a person takes (prompt 2.2): the CMS without a session sends them to the site's sign-in page, which
    /// renders through the gateway; after signing in, the same address serves the CMS bundle.
    /// </summary>
    [Fact]
    public async Task ANavigationToTheCmsGoesThroughSignInAndBack()
    {
        using var browser = Browser(out _, followRedirects: false);

        // Not signed in: an HTML navigation is redirected to /signin, never shown the CMS (ADR 0024).
        var gated = await browser.GetAsync("/admin/", "text/html");
        Assert.Equal(HttpStatusCode.Redirect, gated.StatusCode);
        Assert.Equal("/signin?returnUrl=%2Fadmin%2F", gated.Headers.Location?.OriginalString);

        // The sign-in page is the site's, server-rendered, and neither indexed nor cached (ADR 0027).
        var signInPage = await browser.GetAsync("/signin?returnUrl=%2Fadmin%2F", "text/html");
        Assert.Equal(HttpStatusCode.OK, signInPage.StatusCode);
        Assert.True(signInPage.Headers.CacheControl?.NoStore, "The sign-in page must be Cache-Control: no-store.");
        var html = await signInPage.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Staff sign-in", html, StringComparison.Ordinal);
        Assert.Contains("noindex", html, StringComparison.Ordinal);

        await SignInAsync(browser);

        var cms = await browser.GetAsync("/admin/", "text/html");
        Assert.Equal(HttpStatusCode.OK, cms.StatusCode);
        var shell = await cms.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<cms-root", shell, StringComparison.Ordinal);
        Assert.Contains("<base href=\"/admin/\"", shell, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every inline script the rendered site carries is allowed by the very policy that response sends (ADR 0039).
    /// </summary>
    /// <remarks>
    /// The hashes in <c>Gateway:Csp:Default</c> are the only thing standing between event replay and a silent
    /// death: two of the three scripts are Angular's, and the <c>__jsaction_bootstrap</c> call embeds the list of
    /// event types the site's own templates bind, so adding a <c>(change)</c> or <c>(keydown)</c> binding anywhere
    /// changes its hash. Nothing in the browser reports that — the script is simply refused and clicks made before
    /// hydration are lost. So the hashes are never asserted as literals here: they are recomputed from the page the
    /// gateway actually served, which is what makes this test notice the drift.
    /// </remarks>
    [Fact]
    public async Task EveryInlineScriptOnTheRenderedSiteIsAllowedByItsOwnCsp()
    {
        using var browser = Browser(out _);

        var home = await browser.GetAsync("/", "text/html");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);

        var html = await home.Content.ReadAsStringAsync(Ct);
        var policy = home.Headers.TryGetValues("Content-Security-Policy", out var values)
            ? string.Join(' ', values)
            : string.Empty;

        Assert.Contains("script-src", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", policy.Split(';')
            .First(directive => directive.TrimStart().StartsWith("script-src", StringComparison.Ordinal)));

        var executable = InlineScripts(html).ToList();

        // The `js` class, Angular's event-dispatch contract and the bootstrap call that arms event replay.
        Assert.Equal(3, executable.Count);

        foreach (var script in executable)
        {
            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(script)));

            Assert.True(
                policy.Contains($"'sha256-{hash}'", StringComparison.Ordinal),
                $"The CSP does not allow an inline script the site renders. Add 'sha256-{hash}' to "
                + $"Gateway:Csp:Default (script-src). The script begins: {script[..Math.Min(80, script.Length)]}");
        }
    }

    /// <summary>
    /// The site's server renders its pages from the site read API, asking the gateway at the origin Aspire hands it
    /// over run mode's HTTPS endpoint (prompt 3.2). A read that failed — no origin, an untrusted certificate, a host
    /// the SSR engine refused — makes the page a 503 <c>no-store</c>, so a 200 here is the whole path working.
    /// </summary>
    [Fact]
    public async Task TheSiteRendersItsPagesFromTheApiThroughTheGateway()
    {
        using var browser = Browser(out _);

        foreach (var path in new[] { "/classes", "/shop", "/memberships", "/exhibitions" })
        {
            var page = await browser.GetAsync(path, "text/html");
            Assert.True(
                page.StatusCode == HttpStatusCode.OK,
                $"{path} answered {(int)page.StatusCode}: the site's server could not read the API through the gateway.");
        }
    }

    /// <summary>The inline scripts a browser executes: no <c>src</c>, and not a data block such as JSON-LD.</summary>
    private static IEnumerable<string> InlineScripts(string html)
    {
        foreach (Match match in Regex.Matches(
            html, "<script([^>]*)>(.*?)</script>", RegexOptions.Singleline))
        {
            var attributes = match.Groups[1].Value;
            if (attributes.Contains("src=", StringComparison.Ordinal)) continue;

            // type="application/json" and type="application/ld+json" are data the browser never executes.
            var type = Regex.Match(attributes, "type=\"([^\"]+)\"").Groups[1].Value;
            if (type.Contains("json", StringComparison.OrdinalIgnoreCase)) continue;

            yield return match.Groups[2].Value;
        }
    }

    [Fact]
    public async Task AWrongPasswordAndAnUnknownAddressGetTheSameAnswer()
    {
        // ADR 0023: the browser must not be able to tell a registered address from an unregistered one.
        using var wrongBrowser = Browser(out _);
        using var unknownBrowser = Browser(out _);

        var wrong = await wrongBrowser.PostAsync(
            "/bff/login", new { email = PlatformFixture.AdminEmail, password = $"wrong-{Guid.NewGuid():N}" });
        var unknown = await unknownBrowser.PostAsync(
            "/bff/login", new { email = $"nobody-{Guid.NewGuid():N}@6311arts.test", password = $"wrong-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        Assert.Null(wrongBrowser.SessionCookie);
        Assert.Null(unknownBrowser.SessionCookie);

        var (wrongCode, wrongTitle) = await ProblemAsync(wrong);
        var (unknownCode, unknownTitle) = await ProblemAsync(unknown);
        Assert.Equal(wrongCode, unknownCode);
        Assert.Equal(wrongTitle, unknownTitle);
    }

    /// <summary>
    /// An outbox message staged by the production DataLayer travels through the real Worker to its handler, which
    /// delivers to Mailpit — exactly once, and inside the request's trace.
    /// </summary>
    /// <remarks>
    /// The message is a password reset for the seeded admin. The test composes the Domain itself and calls the
    /// Identity facade against <c>appdb</c>, as the API would, so the message's trace is this test's own;
    /// <see cref="AForgottenPasswordIsResetThroughTheEmailedLink"/> walks the same email from the browser. That proves the DataLayer's stage-then-save-once, the
    /// Worker's dispatch and the keyed handler; it does not exercise the API's own composition root, which the
    /// endpoint tests and the sign-in tests above do.
    /// </remarks>
    [Fact]
    public async Task AnOutboxMessageTravelsThroughTheWorkerToItsHandlerInTheRequestsTrace()
    {
        var connectionString = await platform.App.GetConnectionStringAsync("appdb", Ct)
            ?? throw new InvalidOperationException("appdb has no connection string.");

        await using var domain = DomainServices(connectionString);
        using var mail = platform.App.CreateHttpClient("mail", "http");

        // Anything another flow already sent to this address is not ours.
        var before = (await MailpitSubjectsToAsync(mail, PlatformFixture.AdminEmail)).Count;

        // The request's trace: the correlation id staged with the message is this trace id (ADR 0019).
        ActivityTraceId traceId;
        using (var request = new Activity("e2e password reset").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            traceId = request.TraceId;

            await using var scope = domain.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IIdentityFacade>()
                .RequestPasswordResetAsync(new PasswordResetRequestViewModel(PlatformFixture.AdminEmail), Ct);
            Assert.True(result.Succeeded);
        }

        // This run's message, found by its correlation id: the GUID's "N" form is the trace id's hex.
        var correlationId = Guid.ParseExact(traceId.ToHexString(), "N");

        await using var reader = domain.CreateAsyncScope();
        var db = reader.ServiceProvider.GetRequiredService<ArtsDbContext>();
        var message = await EventuallyAsync(() => db.OutboxMessages.AsNoTracking()
            .Where(candidate => candidate.CorrelationId == correlationId
                && candidate.Type == EmailSendRequested.MessageType
                && candidate.Status == OutboxMessageStatus.Completed)
            .SingleOrDefaultAsync(Ct));

        Assert.Equal(1, message.Attempts);
        Assert.Null(message.LastError);
        Assert.NotNull(message.TraceParent);
        Assert.Contains(traceId.ToHexString(), message.TraceParent, StringComparison.Ordinal);

        // Exactly once, deterministically: the handler's ledger has one row for this message, and it sent.
        var deliveries = await db.EmailDeliveries.AsNoTracking()
            .Where(delivery => delivery.OutboxMessageId == message.Id)
            .ToListAsync(Ct);
        Assert.True(Assert.Single(deliveries).Sent);

        // And in the mailbox: one new message, still one after the Worker has had two more polls to repeat itself.
        await Task.Delay(OutboxPolicy.PollingInterval * 2, Ct);
        var subjects = await MailpitSubjectsToAsync(mail, PlatformFixture.AdminEmail);
        Assert.Equal(before + 1, subjects.Count);
        Assert.Contains("reset", subjects[^1], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A forgotten password, end to end through the browser's only way in: <c>/bff/forgot-password</c>, the gateway's
    /// dispatcher, the API, the outbox, the Worker, Mailpit, the emailed link, <c>/bff/reset-password</c>, and a
    /// sign-in with the new password (ADR 0028).
    /// </summary>
    /// <remarks>
    /// A throwaway member, created through the Domain as a later admin screen would: resetting the seeded admin
    /// would change the password every other test in this class signs in with. A member, not staff, because an
    /// unconfirmed staff account could not sign in afterwards, and this test is about the reset.
    /// </remarks>
    [Fact]
    public async Task AForgottenPasswordIsResetThroughTheEmailedLink()
    {
        var email = $"reset-{Guid.NewGuid():N}@6311arts.test";
        const string newPhrase = "an-e2e-passphrase-long-enough";
        const string secondPhrase = "an-e2e-passphrase-chosen-twice";

        var connectionString = await platform.App.GetConnectionStringAsync("appdb", Ct)
            ?? throw new InvalidOperationException("appdb has no connection string.");
        await using (var domain = DomainServices(connectionString))
        await using (var scope = domain.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ArtsUser>>();
            var member = new ArtsUser
            {
                UserName = email,
                Email = email,
                DisplayName = "E2E Member",
                CreatedAt = TimeProvider.System.GetUtcNow(),
            };

            Assert.True((await users.CreateAsync(member, $"original-{Guid.NewGuid():N}")).Succeeded);
            Assert.True((await users.AddToRoleAsync(member, "Member")).Succeeded);
        }

        using var browser = Browser(out _);
        using var mail = platform.App.CreateHttpClient("mail", "http");

        var requested = await browser.PostAsync("/bff/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);

        // The link as the person receives it, with the user id and token in the fragment (ADR 0023).
        var link = new Uri(await EventuallyAsync(() => ResetLinkToAsync(mail, email)));
        Assert.Equal("/account/reset-password", link.AbsolutePath);
        Assert.Empty(link.Query);

        var fragment = link.Fragment.TrimStart('#').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

        var reset = await browser.PostAsync(
            "/bff/reset-password",
            new { userId = fragment["userId"], token = fragment["token"], newPassword = newPhrase });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // The link is spent: the stamp it carried has moved.
        var again = await browser.PostAsync(
            "/bff/reset-password",
            new { userId = fragment["userId"], token = fragment["token"], newPassword = secondPhrase });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("auth.token_invalid", (await ProblemAsync(again)).Code);

        using var signIn = Browser(out _);
        var login = await signIn.PostAsync("/bff/login", new { email, password = newPhrase });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    /// <summary>A browser on the gateway's HTTPS endpoint, and the jar holding its cookies.</summary>
    /// <summary>
    /// The edge's default body limit (ADR 0030), which only real Kestrel enforces: a 2 MB body to a route that reads
    /// one is refused before it reaches anything that would buffer it.
    /// </summary>
    [Fact]
    public async Task ABodyOverTheDefaultLimitIsRefusedAtTheEdge()
    {
        using var browser = Browser(out _);
        await browser.EnsureAntiforgeryAsync();

        // Kestrel answers 413 as soon as the endpoint starts reading a body whose Content-Length is over the limit, and
        // closes the connection; a client still sending the rest may see that as a reset instead of the 413. Either
        // is the refusal under test — what must not happen is a 2xx or a 4xx from the endpoint itself.
        HttpStatusCode? status;
        try
        {
            var response = await browser.PostAsync(
                "/bff/forgot-password", new { email = new string('a', 2 * 1024 * 1024) + "@6311arts.test" });
            status = response.StatusCode;
        }
        catch (HttpRequestException exception) when (exception.InnerException is IOException)
        {
            status = null;
        }

        Assert.True(status is null or HttpStatusCode.RequestEntityTooLarge, $"Expected a refusal, got {status}.");

        // The control: the same route with an ordinary body is answered, so the refusal above was the size.
        using var control = Browser(out _);
        var accepted = await control.PostAsync("/bff/forgot-password", new { email = "nobody@6311arts.test" });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    /// <summary>
    /// Prompt 2.3's demo as a test: a signed-in Admin uploads an image larger than the default body limit through the
    /// gateway, the API stores its compressed master, and the Worker turns the staged <c>media.rendition-requested</c>
    /// into six renditions that the gateway then serves to staff and refuses to anonymous visitors (a draft).
    /// </summary>
    [Fact]
    public async Task AnUploadedImageGetsItsRenditionsThroughTheWorker()
    {
        using var browser = Browser(out _);
        await SignInAsync(browser);
        await browser.EnsureAntiforgeryAsync();

        // Over 1 MiB on the wire (trailing bytes after the image's end marker, which the master drops), so the upload
        // route's raised limit is what lets it through.
        byte[] photo = [.. Media.Infrastructure.ImageFixtures.Jpeg(2000, 1200, exifTiff: Media.Infrastructure.ImageFixtures.ExifWithGps()),
            .. new byte[1536 * 1024]];
        using var form = new MultipartFormDataContent
        {
            { new StringContent("A gradient with a red block in its top-left corner"), "altText" },
        };
        var file = new ByteArrayContent(photo);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "e2e-gradient.jpg");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cms/media") { Content = form };
        upload.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", browser.RequestToken);

        var created = await browser.Http.SendAsync(upload, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString();

        JsonElement asset = default;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(90))
        {
            asset = await (await browser.GetAsync($"/api/v1/cms/media/{id}", "application/json")).Content
                .ReadFromJsonAsync<JsonElement>(Ct);
            if (asset.GetProperty("processingStatus").GetString() != "Pending")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        }

        output.WriteLine($"Renditions after {deadline.Elapsed.TotalSeconds:F1}s.");
        Assert.Equal("Ready", asset.GetProperty("processingStatus").GetString());
        var renditions = asset.GetProperty("renditions").EnumerateArray().ToList();
        Assert.Equal(6, renditions.Count);
        Assert.True(asset.GetProperty("bytes").GetInt64() < photo.Length);

        var url = renditions.Single(rendition => rendition.GetProperty("name").GetString() == "800.webp")
            .GetProperty("url").GetString()!;
        var staff = await browser.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, staff.StatusCode);
        Assert.Equal("image/webp", staff.Content.Headers.ContentType?.MediaType);

        using var visitor = Browser(out _);
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(url)).StatusCode);
    }

    /// <summary>
    /// Prompt 3.4's checkpoint: staff publish a class, edit it and unpublish it through the CMS, and a signed-out
    /// visitor's page follows each step within <see cref="PublishToPageBudget"/>. The path is the whole platform: the
    /// API's save stages <c>site.revalidate-requested</c>, the Worker clears the tags and publishes them on the cache
    /// invalidation bus, the API drops its copies, and the site's server renders from the API through the gateway.
    /// </summary>
    /// <remarks>
    /// Before ADR 0049 the Worker's invalidation never reached the API (HybridCache keeps each tag's time from its
    /// first L2 read), and these steps took up to the cache's five-minute lifetime.
    /// </remarks>
    [Fact]
    public async Task APublishedClassReachesTheSitePageAndUnpublishingMakesIt404()
    {
        using var staff = Browser(out _);
        await SignInAsync(staff);
        await staff.EnsureAntiforgeryAsync();
        using var visitor = Browser(out _);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var instructor = await CmsAsync(staff, HttpMethod.Post, "/api/v1/cms/instructors", null,
            new { name = $"E2E Instructor {suffix}" });
        Assert.Equal(HttpStatusCode.Created, instructor.Response.StatusCode);

        var startsAt = DateTimeOffset.UtcNow.AddDays(30);
        var created = await CmsAsync(staff, HttpMethod.Post, "/api/v1/cms/classes", null, new
        {
            title = $"E2E Publish {suffix}",
            summary = "Before the edit.",
            runs = new[]
            {
                new
                {
                    capacity = 10,
                    instructorId = instructor.Body.GetProperty("id").GetString(),
                    startingPriceCents = 2500,
                    sessions = new[] { new { startsAt, endsAt = startsAt.AddHours(3) } },
                },
            },
        });
        Assert.Equal(HttpStatusCode.Created, created.Response.StatusCode);
        var id = created.Body.GetProperty("id").GetString();
        var page = $"/classes/{created.Body.GetProperty("slug").GetString()}";

        async Task<(HttpStatusCode Status, string Html)> VisitAsync()
        {
            var response = await visitor.GetAsync(page, "text/html");
            return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
        }

        var published = await CmsAsync(staff, HttpMethod.Post, $"/api/v1/cms/classes/{id}/publish", created.ETag);
        Assert.Equal(HttpStatusCode.OK, published.Response.StatusCode);
        var shown = await WithinBudgetAsync("publish → page", async () =>
            await VisitAsync() is { Status: HttpStatusCode.OK } visit && visit.Html.Contains("Before the edit.", StringComparison.Ordinal));

        var marker = $"Edited {suffix}.";
        var edited = await CmsAsync(staff, HttpMethod.Patch, $"/api/v1/cms/classes/{id}", published.ETag,
            new { summary = marker });
        Assert.Equal(HttpStatusCode.OK, edited.Response.StatusCode);
        var editShown = await WithinBudgetAsync("edit → page", async () =>
            (await VisitAsync()).Html.Contains(marker, StringComparison.Ordinal));

        var unpublished = await CmsAsync(staff, HttpMethod.Post, $"/api/v1/cms/classes/{id}/unpublish", edited.ETag);
        Assert.Equal(HttpStatusCode.OK, unpublished.Response.StatusCode);
        var gone = await WithinBudgetAsync("unpublish → 404", async () =>
            (await VisitAsync()).Status == HttpStatusCode.NotFound);

        output.WriteLine(
            $"Publish → page {shown.TotalSeconds:F1}s; edit → page {editShown.TotalSeconds:F1}s; unpublish → 404 {gone.TotalSeconds:F1}s.");
    }


    /// <summary>Polls once a second until the probe holds; fails if it does not within the budget.</summary>
    private static async Task<TimeSpan> WithinBudgetAsync(string step, Func<Task<bool>> probe)
    {
        // Once a second: the gateway's api limit is per address, and the site server's own reads count against it.
        var clock = Stopwatch.StartNew();
        while (!await probe())
        {
            if (clock.Elapsed > PublishToPageBudget)
            {
                Assert.Fail($"{step} did not happen within {PublishToPageBudget.TotalSeconds:F0}s.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), Ct);
        }

        return clock.Elapsed;
    }

    /// <summary>A CMS write as the admin sends it: antiforgery, and <c>If-Match</c> when the record has a version.</summary>
    private static async Task<(HttpResponseMessage Response, JsonElement Body, string? ETag)> CmsAsync(
        BffClient browser, HttpMethod method, string path, string? ifMatch, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.TryAddWithoutValidation("X-XSRF-TOKEN", browser.RequestToken);
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        var response = await browser.Http.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var json = text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default;
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} answered {(int)response.StatusCode}: {text}");

        return (response, json, response.Headers.ETag?.ToString());
    }

    private BffClient Browser(out CookieJarHandler jar, bool followRedirects = true)
    {
        var handler = DevCertificateHandler.Create();
        handler.AllowAutoRedirect = followRedirects;
        jar = new CookieJarHandler(handler);
        return new BffClient(new HttpClient(jar) { BaseAddress = platform.App.GetEndpoint("gateway", GatewayEndpoint) });
    }

    private async Task<SessionBody> SignInAsync(BffClient browser)
    {
        var response = await browser.PostAsync(
            "/bff/login", new { email = PlatformFixture.AdminEmail, password = platform.AdminSecret });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionBody>(Ct))!;
    }

    /// <summary>The parts of a gateway problem that must not vary: the code and the title. Never the correlation id.</summary>
    private static async Task<(string? Code, string? Title)> ProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = document.RootElement;

        return (root.GetProperty("code").GetString(), root.GetProperty("title").GetString());
    }

    /// <summary>The Domain as the API composes it, over the platform's own database.</summary>
    private static ServiceProvider DomainServices(string connectionString)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<ArtsDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
            ArtsDbContextOptions.Configure(options);
        });
        services.AddArtsKernel();
        services.AddIdentityModule(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The subjects of every message Mailpit holds for this address, oldest first.</summary>
    private static async Task<IReadOnlyList<string>> MailpitSubjectsToAsync(HttpClient mail, string address)
    {
        using var document = JsonDocument.Parse(await mail.GetStringAsync("/api/v1/messages", Ct));

        // Mailpit lists newest first.
        return
        [
            .. document.RootElement.GetProperty("messages").EnumerateArray()
                .Where(message => message.GetProperty("To").EnumerateArray()
                    .Any(to => string.Equals(
                        to.GetProperty("Address").GetString(), address, StringComparison.OrdinalIgnoreCase)))
                .Select(message => message.GetProperty("Subject").GetString() ?? string.Empty)
                .Reverse(),
        ];
    }

    /// <summary>The reset link in the newest message Mailpit holds for this address, or null while there is none.</summary>
    private static async Task<string?> ResetLinkToAsync(HttpClient mail, string address)
    {
        using var list = JsonDocument.Parse(await mail.GetStringAsync("/api/v1/messages", Ct));

        // Mailpit lists newest first.
        var id = list.RootElement.GetProperty("messages").EnumerateArray()
            .Where(message => message.GetProperty("To").EnumerateArray()
                .Any(to => string.Equals(to.GetProperty("Address").GetString(), address, StringComparison.OrdinalIgnoreCase)))
            .Select(message => message.GetProperty("ID").GetString())
            .FirstOrDefault();

        if (id is null)
        {
            return null;
        }

        using var message = JsonDocument.Parse(await mail.GetStringAsync($"/api/v1/message/{id}", Ct));
        var text = message.RootElement.GetProperty("Text").GetString() ?? string.Empty;

        return System.Text.RegularExpressions.Regex.Match(text, @"https?://\S+/account/reset-password#\S+") is
            { Success: true } match
            ? match.Value
            : null;
    }

    /// <summary>Polls until the probe returns a value: the Worker drains on its own timer, not on ours.</summary>
    private static async Task<T> EventuallyAsync<T>(Func<Task<T?>> probe)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);

        while (true)
        {
            if (await probe() is { } value)
            {
                return value;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The expected outcome did not appear within a minute.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }
    }

    private sealed record SessionBody(string DisplayName, IReadOnlyList<string> Roles);
}
