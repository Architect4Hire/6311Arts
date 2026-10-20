using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The shop routes at the edge: which token gets in, what reaches the facade, and the statuses and headers that come
/// back. The rules themselves are tested against the real module in <c>Shop/</c>.
/// </summary>
public sealed class CmsShopEndpointTests
{
    private const string Base = "/api/v1/cms/shop";
    private const string Subject = "11111111-1111-1111-1111-111111111111";

    /// <summary>The one category the create bodies below send, hoisted so the analyzer sees one array.</summary>
    private static readonly string[] Jewelry = ["jewelry"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorCreatesAndGets201WithALocationAndAnETag()
    {
        var facade = new RecordingShopFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(
            Base,
            new
            {
                title = "Copper Cuff",
                categories = Jewelry,
                sku = "ART-9",
                oneOfAKind = true,
                startingPriceCents = 11000,
            },
            Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"{Base}/{RecordingShopFacade.Item.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(RecordingShopFacade.Item.ETag, response.Headers.ETag?.ToString());
        Assert.Equal("Copper Cuff", facade.Created!.Title);
        Assert.Equal(["jewelry"], facade.Created.Categories);
        Assert.Equal((true, 11000L, "ART-9"), (facade.Created.OneOfAKind, facade.Created.StartingPriceCents, facade.Created.Sku));
        Assert.Equal(Subject, facade.Actor);
    }

    [Fact]
    public async Task ACreateWithoutATitleIsAValidationFailureThatNeverReachesTheFacade()
    {
        var facade = new RecordingShopFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(Base, new { categories = Jewelry }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(response));
        Assert.Contains("title", await ErrorFieldsAsync(response));
        Assert.Null(facade.Created);
    }

    [Fact]
    public async Task WhatSquareOwnsIsNotAnEditableField()
    {
        // square.md, ADR 0032: price, stock and sold-out are projections. An unknown member is ignored rather than
        // honoured, so a client sending them changes nothing.
        var facade = new RecordingShopFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(
            client,
            $"{Base}/{RecordingShopFacade.Item.Id}",
            """{"priceCents":1,"stockOnHand":2,"soldOut":true,"status":"Published","title":"Cuff"}""",
            "\"etag\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ShopItemUpdateViewModel(Title: "Cuff"), facade.Updated);
    }

    [Fact]
    public async Task APatchCarriesItsIfMatchToTheFacadeAndAnswersWithTheNewETag()
    {
        var facade = new RecordingShopFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(
            client, $"{Base}/{RecordingShopFacade.Item.Id}", """{"removeArtist":true}""", "\"abc\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"abc\"", facade.IfMatch);
        Assert.Equal(new ShopItemUpdateViewModel(RemoveArtist: true), facade.Updated);
        Assert.Equal(RecordingShopFacade.Item.ETag, response.Headers.ETag?.ToString());
    }

    [Theory]
    [InlineData(ErrorCodes.ConcurrencyConflict, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.PreconditionRequired, (HttpStatusCode)428)]
    [InlineData(ContentErrorCodes.SlugTaken, HttpStatusCode.Conflict)]
    [InlineData(CatalogErrorCodes.SkuTaken, HttpStatusCode.Conflict)]
    [InlineData(ContentErrorCodes.StatusTransitionInvalid, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.NotFound, HttpStatusCode.NotFound)]
    public async Task AFacadeRefusalAnswersWithItsCodesStatusAndNoETag(string code, HttpStatusCode status)
    {
        var facade = new RecordingShopFacade { Error = OperationError.Of(code, "Refused.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var patch = await PatchAsync(
            client, $"{Base}/{RecordingShopFacade.Item.Id}", """{"title":"x"}""", "\"stale\"");
        using var publish = await client.SendAsync(Command("publish", "\"stale\""), Ct);

        foreach (var response in new[] { patch, publish })
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await CodeAsync(response));
            Assert.Null(response.Headers.ETag);
        }
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("unpublish")]
    [InlineData("unschedule")]
    public async Task AnEditorRunsTheStatusCommands(string command)
    {
        var facade = new RecordingShopFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Command(command, "\"v1\""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(command, facade.Command);
        Assert.Equal("\"v1\"", facade.IfMatch);
        Assert.Equal(RecordingShopFacade.Item.ETag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task ArchiveIsForAdminsOnly()
    {
        var facade = new RecordingShopFacade();
        using var api = Factory(facade);

        using (var editor = Client(api, "Editor"))
        using (var refused = await editor.SendAsync(Command("archive", "\"v1\""), Ct))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Null(facade.Command);
        }

        using var admin = Client(api, "Admin");
        using var allowed = await admin.SendAsync(Command("archive", "\"v1\""), Ct);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal("archive", facade.Command);
    }

    [Fact]
    public async Task AMemberAndAnAnonymousCallerAreKeptOut()
    {
        using var api = Factory(new RecordingShopFacade());

        using var member = Client(api, "Member");
        using var forbidden = await member.GetAsync(Base, Ct);
        using var anonymous = api.CreateJsonClient();
        using var unauthenticated = await anonymous.GetAsync(Base, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task AListRowSaysWhichCurrencyItsAmountsAreInAndNeverWhichArtistToAskAbout()
    {
        var facade = new RecordingShopFacade { Page = [RecordingShopFacade.Row] };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync(Base, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"currency\":\"USD\"", body, StringComparison.Ordinal);

        // Which artist to ask Artists about is the module's own question, never part of the answer (ADR 0033).
        Assert.DoesNotContain("artistId", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheListPassesItsQueryThroughIncludingTheCategory()
    {
        var facade = new RecordingShopFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync($"{Base}?limit=5&status=Published&search=cuff&category=jewelry", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ShopItemListViewModel(null, 5, PublishStatus.Published, "cuff", "jewelry"), facade.Listed);
    }

    [Fact]
    public async Task AnItemIsWrittenWithTheContractsTimesAndMoney()
    {
        var facade = new RecordingShopFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync($"{Base}/{RecordingShopFacade.Item.Id}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // api-contract.md: "+00:00", never "Z"; integer cents with a currency.
        Assert.Contains("\"createdAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
        Assert.Contains("\"updatedAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
        Assert.Contains("\"startingPriceCents\":11000", body, StringComparison.Ordinal);
        Assert.Contains("\"currency\":\"USD\"", body, StringComparison.Ordinal);
        Assert.Contains("\"squareHasRecord\":false", body, StringComparison.Ordinal);
        Assert.Equal(RecordingShopFacade.Item.ETag, response.Headers.ETag?.ToString());
    }

    private static HttpRequestMessage Command(string command, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{RecordingShopFacade.Item.Id}/{command}");
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string url, string json, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request, Ct);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.GetProperty("code").GetString();
    }

    private static async Task<List<string>> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return [.. document.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name)];
    }

    private static ApiFactory Factory(RecordingShopFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IShopFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    /// <summary>The facade as far as the controller is concerned: what it was handed, and one chosen answer.</summary>
    private sealed class RecordingShopFacade : IShopFacade
    {
        public static readonly ShopItemServiceModel Item = new(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            "Copper Cuff",
            "copper-cuff",
            "<p>A forged copper cuff.</p>",
            new ShopItemArtistServiceModel(
                Guid.Parse("0199f0a0-0000-7000-8000-0000000000a1"), "Carrie Haase", "carrie-haase", PublishStatus.Published),
            ["jewelry"],
            [],
            [],
            "ART-9",
            9,
            OneOfAKind: true,
            StartingPriceCents: 11000,
            StartingStockQuantity: 1,
            CatalogPolicy.Currency,
            StartingPriceEditable: true,
            StartingStockEditable: true,
            PriceCents: null,
            StockOnHand: null,
            SoldOut: false,
            SquareHasRecord: false,
            PublishStatus.Draft,
            null,
            null,
            SlugLocked: false,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "editor-1",
            EntityTag.Format(Guid.Parse("0123456789abcdef0123456789abcdef")));

        /// <summary>One row for the list to answer with, so its wire shape can be read.</summary>
        public static readonly ShopItemSummaryServiceModel Row = new(
            Item.Id,
            Item.Title,
            Item.Slug,
            Item.Status,
            Item.Artist,
            Item.Categories,
            LeadImageMediaId: null,
            Item.Sku,
            Item.OneOfAKind,
            PriceCents: 12000,
            StockOnHand: 1,
            SoldOut: false,
            CatalogPolicy.Currency,
            Item.UpdatedAt);

        public OperationError? Error { get; init; }

        /// <summary>The rows the list answers with.</summary>
        public IReadOnlyList<ShopItemSummaryServiceModel> Page { get; init; } = [];

        public ShopItemCreateViewModel? Created { get; private set; }

        public ShopItemUpdateViewModel? Updated { get; private set; }

        public ShopItemListViewModel? Listed { get; private set; }

        public ContentScheduleViewModel? Schedule { get; private set; }

        public string? Command { get; private set; }

        public string? IfMatch { get; private set; }

        public string? Actor { get; private set; }

        public Task<OperationResult<CursorPage<ShopItemSummaryServiceModel>>> ListAsync(
            ShopItemListViewModel request, CancellationToken cancellationToken)
        {
            Listed = request;
            return Task.FromResult(Error is null
                ? OperationResult.Success(new CursorPage<ShopItemSummaryServiceModel>(Page, null))
                : OperationResult.Failure<CursorPage<ShopItemSummaryServiceModel>>(Error));
        }

        public Task<OperationResult<ShopItemServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) => Answer();

        public Task<OperationResult<ShopItemServiceModel>> CreateAsync(
            ShopItemCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
        {
            (Created, Actor) = (create, actorUserId);
            return Answer();
        }

        public Task<OperationResult<ShopItemServiceModel>> UpdateAsync(
            Guid id, ShopItemUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            (Updated, IfMatch, Actor) = (update, ifMatch, actorUserId);
            return Answer();
        }

        public Task<OperationResult<ShopItemServiceModel>> PublishAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("publish", ifMatch, actorUserId);

        public Task<OperationResult<ShopItemServiceModel>> UnpublishAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unpublish", ifMatch, actorUserId);

        public Task<OperationResult<ShopItemServiceModel>> ArchiveAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("archive", ifMatch, actorUserId);

        public Task<OperationResult<ShopItemServiceModel>> ScheduleAsync(
            Guid id,
            ContentScheduleViewModel schedule,
            string? ifMatch,
            string actorUserId,
            CancellationToken cancellationToken)
        {
            Schedule = schedule;
            return Run("schedule", ifMatch, actorUserId);
        }

        public Task<OperationResult<ShopItemServiceModel>> UnscheduleAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unschedule", ifMatch, actorUserId);

        // The Worker's, not a route's.
        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Task<OperationResult<ShopItemServiceModel>> Run(string command, string? ifMatch, string actorUserId)
        {
            (Command, IfMatch, Actor) = (command, ifMatch, actorUserId);
            return Answer();
        }

        private Task<OperationResult<ShopItemServiceModel>> Answer() => Task.FromResult(
            Error is null ? OperationResult.Success(Item) : OperationResult.Failure<ShopItemServiceModel>(Error));
    }
}
