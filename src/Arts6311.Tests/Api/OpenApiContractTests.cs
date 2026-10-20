using System.Text.Json;
using System.Text.Json.Nodes;
using Arts6311.Tests.Api.Infrastructure;

namespace Arts6311.Tests.Api;

/// <summary>
/// The OpenAPI document is part of the API contract (api-contract.md "Compatibility"): any shape change fails
/// here until the snapshot is regenerated in the same change, so the diff is reviewed rather than discovered by a
/// client.
/// </summary>
/// <remarks>
/// To regenerate: <c>UPDATE_OPENAPI_SNAPSHOT=1 dotnet test</c>, then read the diff. Removing or renaming a field,
/// changing nullability, narrowing accepted values or changing ordering is a breaking change and needs a new
/// version or a migration path — the snapshot diff is where that conversation starts.
/// </remarks>
public sealed class OpenApiContractTests
{
    private const string UpdateVariable = "UPDATE_OPENAPI_SNAPSHOT";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public async Task TheDocumentMatchesTheSnapshot()
    {
        var document = await FetchDocument();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, document, TestContext.Current.CancellationToken);
            return;
        }

        Assert.True(
            File.Exists(SnapshotPath),
            $"The OpenAPI snapshot is missing. Create it with {UpdateVariable}=1 dotnet test, then review the diff.");

        var snapshot = await File.ReadAllTextAsync(SnapshotPath, TestContext.Current.CancellationToken);

        Assert.Equal(Normalize(snapshot), document, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public async Task TheComparisonFailsOnAChangedShape()
    {
        // Without this, a green run above could equally mean "the contract is unchanged" or "the check is asleep".
        // Here the document is mutated the way a real change would mutate it — a new response field — and the same
        // comparison must reject it.
        var document = await FetchDocument();
        var mutated = JsonNode.Parse(document)!;

        mutated["components"]!["schemas"]!["HealthInfoServiceModel"]!["properties"]!["smuggledField"] =
            new JsonObject { ["type"] = "string" };

        Assert.NotEqual(document, Normalize(mutated.ToJsonString()));
    }

    [Fact]
    public async Task TheDocumentDescribesTheRoutesTheApiActuallyHas()
    {
        // Guards the snapshot itself: an empty or truncated document would otherwise match a snapshot taken from
        // the same emptiness.
        var paths = JsonNode.Parse(await FetchDocument())!["paths"]!.AsObject();

        Assert.Contains("/api/v1/site/health-info", paths.Select(path => path.Key));
    }

    [Fact]
    public async Task TheDocumentOffersJsonOnly()
    {
        // MVC's default formatters also advertise text/plain and text/json for every response, which lets a
        // generated client type a body as string. Removing a media type once a client exists is a narrowing, so
        // the set is pinned while there is no client.
        var document = await FetchDocument();

        Assert.DoesNotContain("text/plain", document, StringComparison.Ordinal);
        Assert.DoesNotContain("text/json", document, StringComparison.Ordinal);
        Assert.Contains("application/json", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProblemSchemaDescribesCodeAndCorrelationId()
    {
        // They are ProblemDetails.Extensions entries, which the generator cannot see on its own. They are the one
        // pair of fields every client branches on, so the document has to name them.
        var properties = JsonNode.Parse(await FetchDocument())!
            ["components"]!["schemas"]!["ProblemDetails"]!["properties"]!.AsObject()
            .Select(property => property.Key)
            .ToList();

        Assert.Contains("code", properties);
        Assert.Contains("correlationId", properties);
    }

    [Fact]
    public async Task TheDocumentDoesNotPublishDevelopmentScaffolding()
    {
        // Routes marked IgnoreApi stay out of the contract: the test host's probes (Development-only scaffolding
        // since the 1.5 ping route was deleted in 1.12) are mapped in this host, so their absence here is the proof.
        var paths = JsonNode.Parse(await FetchDocument())!["paths"]!.AsObject().Select(path => path.Key).ToList();

        Assert.DoesNotContain("/" + CmsAdminProbeController.Route, paths);
        Assert.DoesNotContain("/" + DevelopmentOnlyProbeController.Route, paths);
    }

    [Fact]
    public async Task TheDocumentNamesNoHost()
    {
        // The snapshot must not depend on the port the test server happened to get, and the public base URL is the
        // gateway's, not the API's (ADR 0001).
        var document = JsonNode.Parse(await FetchDocument())!.AsObject();

        Assert.True(
            !document.TryGetPropertyValue("servers", out var servers) || servers!.AsArray().Count == 0,
            "The OpenAPI document should carry no servers entry.");
    }

    [Fact]
    public async Task TheDocumentIsNotServedOutsideDevelopment()
    {
        // gateway.md: the docs are proxied in Development only. The API must not publish them anywhere else, since
        // the gateway is not the only thing that could ask.
        using var api = new ProductionApiFactory();
        using var client = api.CreateJsonClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string SnapshotPath =>
        Path.Combine(RepoPaths.SourceRoot(), "Arts6311.Tests", "Api", "Snapshots", "openapi-v1.json");

    private static async Task<string> FetchDocument()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return Normalize(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Re-serializes indented so the snapshot is a reviewable diff rather than one long line. Property order is
    /// left as the generator produced it: order is part of the contract (api-contract.md), so sorting here would
    /// hide a reordering instead of surfacing it.
    /// </summary>
    private static string Normalize(string json) =>
        JsonSerializer.Serialize(JsonNode.Parse(json), Indented);
}
