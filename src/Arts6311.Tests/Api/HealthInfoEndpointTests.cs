using System.Net;
using System.Text.Json;
using Arts6311.Domain.Managers.Time;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Api;

/// <summary>
/// <c>GET /api/v1/site/health-info</c>: the sample route the API conventions were built against, and the first
/// public site read.
/// </summary>
public sealed class HealthInfoEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ItAnswersAnonymouslyWithTheStatusAndServerTime()
    {
        using var api = Factory();
        using var client = api.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal(Now, body.GetProperty("serverTimeUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task ItSaysNothingAboutTheHost()
    {
        // The route is anonymous and reachable from the internet through the gateway. A version, machine name or
        // environment name here would be free reconnaissance, so the shape is pinned to exactly two fields.
        using var api = Factory();
        using var client = api.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;

        Assert.Equal(
            ["status", "serverTimeUtc"],
            body.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public async Task TheTimeIsWrittenAsAZeroOffsetIso8601Instant()
    {
        // The OpenAPI document says only "format: date-time", which cannot tell "+00:00" from "Z", and
        // GetDateTimeOffset() is offset-insensitive — so neither the snapshot nor the test above would notice the
        // wire form changing. api-contract.md promises UTC, and this pins which UTC spelling clients will see.
        using var api = Factory();
        using var client = api.CreateJsonClient();

        var response = await client.GetAsync("/api/v1/site/health-info", TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"serverTimeUtc\":\"2026-10-19T18:30:00+00:00\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItIsAReadAndNothingElse()
    {
        using var api = Factory();
        using var client = api.CreateJsonClient();

        var response = await client.PostAsync(
            "/api/v1/site/health-info", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        // The edge's own refusals are ProblemDetails too, with their own stable code.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).RootElement;
        Assert.Equal("request.method_not_allowed", problem.GetProperty("code").GetString());
    }

    private static ApiFactory Factory() => new(
        configureServices: services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now)));
}
