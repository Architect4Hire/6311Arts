using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The shop import's routes at the edge (ADR 0053): who gets in, what reaches the facade — the file, its name, the
/// idempotency key, the actor — and what comes back. The rules are tested against the real module in <c>Shop/</c>.
/// </summary>
public sealed class CmsShopImportEndpointTests
{
    private const string Base = "/api/v1/cms/shop/import";
    private const string Subject = "11111111-1111-1111-1111-111111111111";
    private const string Csv = "title\nBlue Heron";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorPreviewsAFileAndGetsEveryRowBack()
    {
        var facade = new RecordingImportFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync($"{Base}/preview", new { csv = Csv, fileName = "artwork.csv" }, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Equal(new ShopImportViewModel(Csv, "artwork.csv"), facade.Previewed);
        using var document = JsonDocument.Parse(body);
        var row = document.RootElement.GetProperty("rows")[0];
        Assert.Equal(2, row.GetProperty("row").GetInt32());
        Assert.True(row.GetProperty("skuSuggested").GetBoolean());
        Assert.Equal("starting_price", row.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task AnImportCarriesItsKeyAndActorToTheFacade()
    {
        var facade = new RecordingImportFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Import("key-123"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("key-123", Subject), (facade.Key, facade.Actor));
        Assert.Equal(Csv, facade.Imported!.Csv);
        Assert.False(response.Headers.Contains(IdempotencyPolicy.ReplayedHeader));
    }

    [Fact]
    public async Task AReplayIsMarkedOnTheResponse()
    {
        var facade = new RecordingImportFacade { Replay = true };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Import("key-123"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["true"], response.Headers.GetValues(IdempotencyPolicy.ReplayedHeader));
    }

    [Fact]
    public async Task ABodyWithoutTheFileIsRefusedBeforeTheFacade()
    {
        var facade = new RecordingImportFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync($"{Base}/preview", new { fileName = "artwork.csv" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(facade.Previewed);
    }

    [Fact]
    public async Task ARowProblemIsA400KeyedByRowAndColumn()
    {
        var facade = new RecordingImportFacade
        {
            Refusal = OperationError.Validation("A row needs fixing.", [("rows[3].sku", "Taken.")]),
        };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Import("key-123"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(ErrorCodes.ValidationFailed, document.RootElement.GetProperty("code").GetString());
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("rows[3].sku", out _));
    }

    [Theory]
    [InlineData("cms.slug_taken")]
    [InlineData("cms.sku_taken")]
    public async Task ASlugOrSkuTakenMidImportIs409(string code)
    {
        var facade = new RecordingImportFacade { Refusal = OperationError.Of(code, "Taken meanwhile.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Import("key-123"), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SeveralKeysCountAsNone()
    {
        // api-contract.md: ambiguous, so neither is used — and the facade decides that a missing key is refused.
        var facade = new RecordingImportFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        using var request = Import("first");
        request.Headers.TryAddWithoutValidation(IdempotencyPolicy.KeyHeader, "second");

        using var response = await client.SendAsync(request, Ct);

        Assert.Null(facade.Key);
    }

    [Fact]
    public async Task AReusedKeyIs422()
    {
        var facade = new RecordingImportFacade { Refusal = OperationError.Of(ErrorCodes.IdempotencyKeyReused, "Used.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Import("key-123"), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AFileOverTheDefaultLimitStillReachesTheFacade()
    {
        // The import's own RequestSizeLimit raises the API's 1 MiB default, as the gateway route does (ADR 0053).
        var facade = new RecordingImportFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        var large = "title\n" + string.Concat(Enumerable.Repeat(new string('x', 1_499) + "\n", 1_000));

        using var response = await client.PostAsJsonAsync($"{Base}/preview", new { csv = large }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(large.Length, facade.Previewed!.Csv.Length);
    }

    [Fact]
    public async Task AMemberAndAnAnonymousCallerAreKeptOut()
    {
        var facade = new RecordingImportFacade();
        using var api = Factory(facade);

        using var member = Client(api, "Member");
        using var forbidden = await member.SendAsync(Import("key-123"), Ct);
        using var anonymous = api.CreateJsonClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync($"{Base}/preview", new { csv = Csv }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Null(facade.Imported);
        Assert.Null(facade.Previewed);
    }

    private static HttpRequestMessage Import(string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Base) { Content = JsonContent.Create(new { csv = Csv }) };
        request.Headers.TryAddWithoutValidation(IdempotencyPolicy.KeyHeader, key);
        return request;
    }

    private static ApiFactory Factory(RecordingImportFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IShopImportFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    /// <summary>The facade as far as the controller is concerned: what it was handed, and one chosen answer.</summary>
    private sealed class RecordingImportFacade : IShopImportFacade
    {
        public ShopImportViewModel? Previewed { get; private set; }

        public ShopImportViewModel? Imported { get; private set; }

        public string? Key { get; private set; }

        public string? Actor { get; private set; }

        public bool Replay { get; init; }

        public OperationError? Refusal { get; init; }

        public Task<OperationResult<ShopImportPreviewServiceModel>> PreviewAsync(
            ShopImportViewModel import, CancellationToken cancellationToken)
        {
            Previewed = import;
            return Task.FromResult(Refusal is null
                ? OperationResult.Success(new ShopImportPreviewServiceModel(1, 1,
                [
                    new ShopImportRowServiceModel(
                        2, "Blue Heron", "blue-heron", "ART-1", true, null, ["prints"], false, null, null,
                        [new ShopImportProblemServiceModel(ShopImportColumns.StartingPrice, "Not a price.")]),
                ]))
                : OperationResult.Failure<ShopImportPreviewServiceModel>(Refusal));
        }

        public Task<IdempotentOutcome<ShopImportResultServiceModel>> ImportAsync(
            ShopImportViewModel import, string? idempotencyKey, string actorUserId, CancellationToken cancellationToken)
        {
            (Imported, Key, Actor) = (import, idempotencyKey, actorUserId);
            var result = Refusal is null
                ? OperationResult.Success(new ShopImportResultServiceModel(
                    1, [new ShopImportCreatedServiceModel(2, Guid.NewGuid(), "Blue Heron", "blue-heron", "ART-1")]))
                : OperationResult.Failure<ShopImportResultServiceModel>(Refusal);
            return Task.FromResult(new IdempotentOutcome<ShopImportResultServiceModel>(result, Replay));
        }
    }
}
