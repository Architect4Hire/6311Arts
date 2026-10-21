using System.Net;
using Arts6311.Gateway.Edge;
using Arts6311.Tests.Gateway.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The named rate-limit policies (<c>gateway.md</c> "Rate limits").
/// </summary>
public sealed class RateLimitTests : IAsyncLifetime
{
    private StubDestination destination = null!;

    public async ValueTask InitializeAsync() => destination = await StubDestination.StartAsync();

    public async ValueTask DisposeAsync() => await destination.DisposeAsync();

    [Fact]
    public void EveryPolicyGatewayMdNamesExists()
    {
        // Keeps the code and the rule in step. Some of these have no route until 2.12 and 5.14; defining
        // them now means those prompts reference a name instead of inventing a limit.
        Assert.Equal(
            ["api", "bff-account", "bff-login", "checkout", "forms", "forms-submissions", "webhooks"],
            RateLimitExtensions.Policies.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheApiRouteRefusesOnceItsWindowIsFull()
    {
        using var gateway = new GatewayFactory(destination)
            .With("Gateway:RateLimits:api:PermitLimit", "3")
            .With("Gateway:RateLimits:api:Window", "00:05:00")
            .With("Gateway:RateLimits:api:QueueLimit", "0");
        using var client = gateway.CreateJsonClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(3, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(2, statuses.Count(status => status == HttpStatusCode.TooManyRequests));

        // The point of the limit: the refused requests never reached the API.
        Assert.Equal(3, destination.Requests);
    }

    [Fact]
    public async Task ThePublicFormsHaveTheirOwnTighterWindow()
    {
        // ADR 0047: anyone may send a form, so both forms routes share the forms policy — a flood of submissions
        // cannot spend the site's api budget, and a busy site cannot lock out the contact form. GET is used only
        // because it needs no antiforgery pair; the limiter sits on the route whatever the method.
        using var gateway = new GatewayFactory(destination)
            .With("Gateway:RateLimits:forms:PermitLimit", "2")
            .With("Gateway:RateLimits:forms:Window", "00:05:00");
        using var client = gateway.CreateJsonClient();

        var statuses = new List<HttpStatusCode>();
        foreach (var path in new[] { "/api/v1/forms/newsletter", "/api/v1/forms/contact", "/api/v1/forms/submissions" })
        {
            var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
        var api = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    [Fact]
    public async Task TheWebhookRouteHasItsOwnWindow()
    {
        // Separate policies so a flood of webhooks cannot exhaust the browser-facing budget, and vice versa.
        using var gateway = new GatewayFactory(destination)
            .With("Gateway:RateLimits:webhooks:PermitLimit", "1")
            .With("Gateway:RateLimits:webhooks:Window", "00:05:00");
        using var client = gateway.CreateJsonClient();

        var first = await client.PostAsync("/webhooks/square", content: null, TestContext.Current.CancellationToken);
        var second = await client.PostAsync("/webhooks/square", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);

        // The api policy is untouched by the webhook flood.
        var api = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }

    [Fact]
    public async Task ARefusalIsAProblemDetailsWithRetryAfter()
    {
        using var gateway = new GatewayFactory(destination)
            .With("Gateway:RateLimits:api:PermitLimit", "1")
            .With("Gateway:RateLimits:api:Window", "00:05:00");
        using var client = gateway.CreateJsonClient();

        await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);
        var refused = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(refused.Headers.RetryAfter);
    }

    [Fact]
    public void AnUndefinedPolicyIsRefusedAtStartup()
    {
        // A route naming a policy that configuration never defines would otherwise fail only on that route, at
        // request time, long after deployment. Checked against the registration directly, because an incomplete
        // configuration section cannot be simulated by overriding one key.
        var options = new GatewayOptions();
        options.GlobalRateLimit.PermitLimit = 100;
        options.RateLimits["api"] = new RateLimitPolicyOptions { PermitLimit = 10 };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddArtsRateLimiter(options));

        // Names the first policy it could not find, so the message says what to add.
        Assert.Contains("bff-login", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingGlobalLimitIsRefusedAtStartup()
    {
        var options = new GatewayOptions();
        foreach (var name in RateLimitExtensions.Policies.All)
        {
            options.RateLimits[name] = new RateLimitPolicyOptions { PermitLimit = 10 };
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddArtsRateLimiter(options));

        Assert.Contains("GlobalRateLimit", exception.Message, StringComparison.Ordinal);
    }
}
