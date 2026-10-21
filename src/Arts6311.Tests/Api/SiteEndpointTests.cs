using System.Net;
using System.Text.Json;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Tests.Api.Infrastructure;
using Arts6311.Tests.Site.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The site's read routes at the edge (ADR 0042), served by the real modules over a SQLite database: anonymous,
/// cacheable for a minute, a 404 nothing keeps, the contract's spelling of times, and none of the CMS's fields. What
/// each read returns and how its cache behaves is tested through the facades in <c>Site/</c>.
/// </summary>
public sealed class SiteEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/site/classes")]
    [InlineData("/api/v1/site/classes?instructor=gwen-schilling")]
    [InlineData("/api/v1/site/classes/etching")]
    [InlineData("/api/v1/site/instructors")]
    [InlineData("/api/v1/site/instructors/gwen-schilling")]
    [InlineData("/api/v1/site/exhibitions")]
    [InlineData("/api/v1/site/exhibitions/spring-show")]
    [InlineData("/api/v1/site/artists")]
    [InlineData("/api/v1/site/artists/jane-doe")]
    [InlineData("/api/v1/site/shop")]
    [InlineData("/api/v1/site/shop?category=prints&artist=jane-doe&sort=price-asc&limit=10")]
    [InlineData("/api/v1/site/shop/etching-plate")]
    [InlineData("/api/v1/site/memberships")]
    [InlineData("/api/v1/site/pages/privacy")]
    [InlineData("/api/v1/site/sitemap")]
    public async Task EverySiteReadIsAnonymousAndCacheableForAMinute(string path)
    {
        await using var host = await SeededHostAsync();
        using var api = Factory(host);
        using var anonymous = api.CreateJsonClient();

        using var response = await anonymous.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/api/v1/site/classes/draft-class")]
    [InlineData("/api/v1/site/instructors/new-teacher")]
    [InlineData("/api/v1/site/exhibitions/nothing-here")]
    [InlineData("/api/v1/site/artists/zed-draft")]
    [InlineData("/api/v1/site/shop/unfinished")]
    [InlineData("/api/v1/site/pages/terms")]
    [InlineData("/api/v1/site/pages/Not_A_Slug")]
    public async Task ADraftOrAnUnknownAddressIsA404NothingKeeps(string path)
    {
        await using var host = await SeededHostAsync();
        using var api = Factory(host);
        using var anonymous = api.CreateJsonClient();

        using var response = await anonymous.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(ErrorCodes.NotFound, document.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("/api/v1/site/shop?sort=cheapest", ErrorCodes.ValidationFailed)]
    [InlineData("/api/v1/site/shop?category=merch", ErrorCodes.ValidationFailed)]
    [InlineData("/api/v1/site/shop?limit=0", ErrorCodes.ValidationFailed)]
    [InlineData("/api/v1/site/shop?cursor=not-a-cursor", ErrorCodes.CursorInvalid)]
    public async Task ABadShopQueryIsA400WithItsCode(string path, string code)
    {
        await using var host = await SeededHostAsync();
        using var api = Factory(host);
        using var anonymous = api.CreateJsonClient();

        using var response = await anonymous.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TimesAreWrittenWithAnExplicitOffsetAndMoneyInCents()
    {
        await using var host = await SeededHostAsync();
        using var api = Factory(host);
        using var anonymous = api.CreateJsonClient();

        var body = await anonymous.GetStringAsync("/api/v1/site/classes/etching", Ct);
        using var document = JsonDocument.Parse(body);
        var run = document.RootElement.GetProperty("runs")[0];

        // api-contract.md: "+00:00", not "Z", which format: date-time cannot tell apart.
        Assert.EndsWith("+00:00", run.GetProperty("startsAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("+00:00", run.GetProperty("sessions")[0].GetProperty("endsAt").GetString(), StringComparison.Ordinal);
        Assert.Equal("America/Chicago", run.GetProperty("displayTimeZone").GetString());
        Assert.Equal(40_00, run.GetProperty("priceCents").GetInt64());
        Assert.Equal("USD", run.GetProperty("currency").GetString());
        Assert.Equal("gwen-schilling", run.GetProperty("instructor").GetProperty("slug").GetString());
    }

    [Theory]
    [InlineData("/api/v1/site/classes")]
    [InlineData("/api/v1/site/classes/etching")]
    [InlineData("/api/v1/site/instructors")]
    [InlineData("/api/v1/site/instructors/gwen-schilling")]
    [InlineData("/api/v1/site/exhibitions")]
    [InlineData("/api/v1/site/exhibitions/spring-show")]
    [InlineData("/api/v1/site/artists")]
    [InlineData("/api/v1/site/artists/jane-doe")]
    [InlineData("/api/v1/site/shop")]
    [InlineData("/api/v1/site/sitemap")]
    [InlineData("/api/v1/site/shop/etching-plate")]
    [InlineData("/api/v1/site/memberships")]
    [InlineData("/api/v1/site/pages/privacy")]
    public async Task NoAnswerCarriesAStaffFieldAStatusOrTheStockCount(string path)
    {
        await using var host = await SeededHostAsync();
        using var api = Factory(host);
        using var anonymous = api.CreateJsonClient();

        var body = await anonymous.GetStringAsync(path, Ct);

        foreach (var field in new[]
        {
            "updatedBy", "etag", "rowVersion", "status", "legacyWordPressId", "sku", "stockOnHand", "startingPriceCents",
            "startingStockQuantity", "scheduledFor", "publishedAt", "squareHasRecord",
        })
        {
            Assert.DoesNotContain($"\"{field}\"", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>One of each, published, with a draft beside most, so a route can be asked for either.</summary>
    private static async Task<SiteTestHost> SeededHostAsync()
    {
        var host = await SiteTestHost.CreateAsync(Ct);
        var artist = await host.AddArtistAsync("Jane Doe");
        await host.AddArtistAsync("Zed Draft", PublishStatus.Draft);
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        await host.AddInstructorAsync("New Teacher", PublishStatus.Draft);
        await host.AddClassAsync("Etching", gwen, runs: [(host.Now.AddDays(3), 40_00, 5, false)]);
        await host.AddClassAsync("Draft Class", PublishStatus.Draft, runs: [(host.Now.AddDays(3), 40_00, 5, false)]);
        await host.AddExhibitionAsync("Spring Show", new DateOnly(2026, 3, 1), artists: [artist]);
        await host.AddShopItemAsync("Etching Plate", artistId: artist, categories: ["prints"]);
        await host.AddShopItemAsync("Unfinished", PublishStatus.Draft);
        await host.AddPlanAsync("Friend", 1);
        await host.AddPageAsync("Privacy");
        await host.AddPageAsync("Terms", PublishStatus.Draft);
        return host;
    }

    /// <summary>
    /// The API with its database, clock and HybridCache L2 swapped for the host's: the real modules and caching run,
    /// but nothing reaches SQL Server or Redis.
    /// </summary>
    private static ApiFactory Factory(SiteTestHost host) =>
        new(configureServices: services =>
        {
            services.RemoveAll<ArtsDbContext>();
            services.AddScoped(_ => host.NewContext());
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(host.Time));
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });
}
