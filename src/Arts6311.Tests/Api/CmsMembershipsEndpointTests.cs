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
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The membership routes at the edge: which token gets in, what reaches the facade, and the statuses and headers
/// that come back. The rules themselves are tested against the real module in <c>Memberships/</c>.
/// </summary>
public sealed class CmsMembershipsEndpointTests
{
    private const string Base = "/api/v1/cms/memberships";
    private const string Subject = "11111111-1111-1111-1111-111111111111";

    /// <summary>The perks the create bodies below send, hoisted so the analyzer sees one array.</summary>
    private static readonly string[] Perks = ["5% off every class & workshop"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorCreatesAndGets201WithALocationAndAnETag()
    {
        var facade = new RecordingMembershipsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(
            Base,
            new
            {
                name = "Gray",
                perks = Perks,
                discountPercent = 5,
                startingPriceCents = 10000,
                startingCadence = "Annual",
            },
            Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"{Base}/{RecordingMembershipsFacade.Plan.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(RecordingMembershipsFacade.Plan.ETag, response.Headers.ETag?.ToString());
        Assert.Equal("Gray", facade.Created!.Name);
        Assert.Equal(Perks, facade.Created.Perks);
        Assert.Equal((5, 10000L, MembershipCadence.Annual), (facade.Created.DiscountPercent, facade.Created.StartingPriceCents, facade.Created.StartingCadence));
        Assert.Equal(Subject, facade.Actor);
    }

    [Fact]
    public async Task ACreateWithoutANameIsAValidationFailureThatNeverReachesTheFacade()
    {
        var facade = new RecordingMembershipsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(Base, new { perks = Perks }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(response));
        Assert.Contains("name", await ErrorFieldsAsync(response));
        Assert.Null(facade.Created);
    }

    [Fact]
    public async Task WhatSquareOwnsIsNotAnEditableField()
    {
        // square.md, ADR 0005: the price and the cadence Square reports are projections. An unknown member is
        // ignored rather than honoured, so a client sending them changes nothing.
        var facade = new RecordingMembershipsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(
            client,
            $"{Base}/{RecordingMembershipsFacade.Plan.Id}",
            """{"priceCents":1,"cadence":"MONTHLY","status":"Published","name":"Gray Plus"}""",
            "\"etag\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new MembershipPlanUpdateViewModel(Name: "Gray Plus"), facade.Updated);
    }

    [Fact]
    public async Task APatchCarriesItsIfMatchToTheFacadeAndAnswersWithTheNewETag()
    {
        var facade = new RecordingMembershipsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(
            client, $"{Base}/{RecordingMembershipsFacade.Plan.Id}", """{"featured":true}""", "\"abc\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"abc\"", facade.IfMatch);
        Assert.Equal(new MembershipPlanUpdateViewModel(Featured: true), facade.Updated);
        Assert.Equal(RecordingMembershipsFacade.Plan.ETag, response.Headers.ETag?.ToString());
    }

    [Theory]
    [InlineData(ErrorCodes.ConcurrencyConflict, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.PreconditionRequired, (HttpStatusCode)428)]
    [InlineData(ContentErrorCodes.SlugTaken, HttpStatusCode.Conflict)]
    [InlineData(ContentErrorCodes.StatusTransitionInvalid, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.NotFound, HttpStatusCode.NotFound)]
    public async Task AFacadeRefusalAnswersWithItsCodesStatusAndNoETag(string code, HttpStatusCode status)
    {
        var facade = new RecordingMembershipsFacade { Error = OperationError.Of(code, "Refused.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var patch = await PatchAsync(
            client, $"{Base}/{RecordingMembershipsFacade.Plan.Id}", """{"name":"x"}""", "\"stale\"");
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
        var facade = new RecordingMembershipsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Command(command, "\"v1\""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(command, facade.Command);
        Assert.Equal("\"v1\"", facade.IfMatch);
        Assert.Equal(RecordingMembershipsFacade.Plan.ETag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task TheScheduleCommandCarriesItsTime()
    {
        var facade = new RecordingMembershipsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        var when = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{RecordingMembershipsFacade.Plan.Id}/schedule")
        {
            Content = new StringContent(
                $$"""{"scheduledFor":"{{when:yyyy-MM-ddTHH:mm:sszzz}}"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("schedule", facade.Command);
        Assert.Equal(when, facade.Schedule!.ScheduledFor);
    }

    [Fact]
    public async Task ArchiveIsForAdminsOnly()
    {
        var facade = new RecordingMembershipsFacade();
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
        using var api = Factory(new RecordingMembershipsFacade());

        using var member = Client(api, "Member");
        using var forbidden = await member.GetAsync(Base, Ct);
        using var anonymous = api.CreateJsonClient();
        using var unauthenticated = await anonymous.GetAsync(Base, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task TheListPassesItsQueryThroughAndSaysWhichCurrencyItsAmountsAreIn()
    {
        var facade = new RecordingMembershipsFacade { Page = [RecordingMembershipsFacade.Row] };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync($"{Base}?limit=5&status=Published&search=gray", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new MembershipPlanListViewModel(null, 5, PublishStatus.Published, "gray"), facade.Listed);
        Assert.Contains("\"currency\":\"USD\"", body, StringComparison.Ordinal);
        Assert.Contains("\"perkCount\":1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APlanIsWrittenWithTheContractsTimesAndMoney()
    {
        var facade = new RecordingMembershipsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync($"{Base}/{RecordingMembershipsFacade.Plan.Id}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // api-contract.md: "+00:00", never "Z"; integer cents with a currency; the cadence by name.
        Assert.Contains("\"createdAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
        Assert.Contains("\"updatedAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
        Assert.Contains("\"startingPriceCents\":10000", body, StringComparison.Ordinal);
        Assert.Contains("\"startingCadence\":\"Annual\"", body, StringComparison.Ordinal);
        Assert.Contains("\"currency\":\"USD\"", body, StringComparison.Ordinal);
        Assert.Contains("\"squareHasRecord\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"sitePath\":\"/memberships\"", body, StringComparison.Ordinal);
        Assert.Equal(RecordingMembershipsFacade.Plan.ETag, response.Headers.ETag?.ToString());
    }

    private static HttpRequestMessage Command(string command, string ifMatch)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"{Base}/{RecordingMembershipsFacade.Plan.Id}/{command}");
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

    private static ApiFactory Factory(RecordingMembershipsFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IMembershipsFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    /// <summary>The facade as far as the controller is concerned: what it was handed, and one chosen answer.</summary>
    private sealed class RecordingMembershipsFacade : IMembershipsFacade
    {
        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<IReadOnlyList<global::Arts6311.Domain.Modules.Memberships.Managers.ServiceModels.SiteMembershipPlanServiceModel>>> ListForSiteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Memberships.Managers.ServiceModels.SiteMembershipPlanServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public static readonly MembershipPlanServiceModel Plan = new(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "Gray",
            "gray-membership",
            ["5% off every class & workshop"],
            DiscountPercent: 5,
            Featured: false,
            DisplayOrder: 1,
            StartingPriceCents: 10_000,
            MembershipCadence.Annual,
            CatalogPolicy.Currency,
            StartingPriceEditable: true,
            PriceCents: null,
            Cadence: null,
            SquareHasRecord: false,
            PublishStatus.Draft,
            null,
            null,
            MembershipPolicy.SitePath,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "editor-1",
            EntityTag.Format(Guid.Parse("0123456789abcdef0123456789abcdef")));

        /// <summary>One row for the list to answer with, so its wire shape can be read.</summary>
        public static readonly MembershipPlanSummaryServiceModel Row = new(
            Plan.Id,
            Plan.Name,
            Plan.Slug,
            Plan.Status,
            Plan.DiscountPercent,
            Plan.Featured,
            Plan.DisplayOrder,
            PerkCount: 1,
            Plan.StartingPriceCents,
            PriceCents: 12_000,
            Cadence: "ANNUAL",
            CatalogPolicy.Currency,
            Plan.UpdatedAt);

        public OperationError? Error { get; init; }

        /// <summary>The rows the list answers with.</summary>
        public IReadOnlyList<MembershipPlanSummaryServiceModel> Page { get; init; } = [];

        public MembershipPlanCreateViewModel? Created { get; private set; }

        public MembershipPlanUpdateViewModel? Updated { get; private set; }

        public MembershipPlanListViewModel? Listed { get; private set; }

        public ContentScheduleViewModel? Schedule { get; private set; }

        public string? Command { get; private set; }

        public string? IfMatch { get; private set; }

        public string? Actor { get; private set; }

        public Task<OperationResult<CursorPage<MembershipPlanSummaryServiceModel>>> ListAsync(
            MembershipPlanListViewModel request, CancellationToken cancellationToken)
        {
            Listed = request;
            return Task.FromResult(Error is null
                ? OperationResult.Success(new CursorPage<MembershipPlanSummaryServiceModel>(Page, null))
                : OperationResult.Failure<CursorPage<MembershipPlanSummaryServiceModel>>(Error));
        }

        public Task<OperationResult<MembershipPlanServiceModel>> GetAsync(
            Guid id, CancellationToken cancellationToken) => Answer();

        public Task<OperationResult<MembershipPlanServiceModel>> CreateAsync(
            MembershipPlanCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
        {
            (Created, Actor) = (create, actorUserId);
            return Answer();
        }

        public Task<OperationResult<MembershipPlanServiceModel>> UpdateAsync(
            Guid id,
            MembershipPlanUpdateViewModel update,
            string? ifMatch,
            string actorUserId,
            CancellationToken cancellationToken)
        {
            (Updated, IfMatch, Actor) = (update, ifMatch, actorUserId);
            return Answer();
        }

        public Task<OperationResult<MembershipPlanServiceModel>> PublishAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("publish", ifMatch, actorUserId);

        public Task<OperationResult<MembershipPlanServiceModel>> UnpublishAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unpublish", ifMatch, actorUserId);

        public Task<OperationResult<MembershipPlanServiceModel>> ArchiveAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("archive", ifMatch, actorUserId);

        public Task<OperationResult<MembershipPlanServiceModel>> ScheduleAsync(
            Guid id,
            ContentScheduleViewModel schedule,
            string? ifMatch,
            string actorUserId,
            CancellationToken cancellationToken)
        {
            Schedule = schedule;
            return Run("schedule", ifMatch, actorUserId);
        }

        public Task<OperationResult<MembershipPlanServiceModel>> UnscheduleAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unschedule", ifMatch, actorUserId);

        // The Worker's, not a route's.
        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScheduledPublishOutcome> PublishScheduledAsync(
            Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Task<OperationResult<MembershipPlanServiceModel>> Run(string command, string? ifMatch, string actorUserId)
        {
            (Command, IfMatch, Actor) = (command, ifMatch, actorUserId);
            return Answer();
        }

        private Task<OperationResult<MembershipPlanServiceModel>> Answer() => Task.FromResult(
            Error is null
                ? OperationResult.Success(Plan)
                : OperationResult.Failure<MembershipPlanServiceModel>(Error));
    }
}
