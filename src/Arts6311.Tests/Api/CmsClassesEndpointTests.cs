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
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The class routes at the edge: which token gets in, what reaches the facade, and the statuses and headers that
/// come back. The rules themselves are tested against the real module in <c>Classes/</c>.
/// </summary>
public sealed class CmsClassesEndpointTests
{
    private const string Base = "/api/v1/cms/classes";
    private const string Subject = "11111111-1111-1111-1111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorCreatesAndGets201WithALocationAndAnETag()
    {
        var facade = new RecordingClassesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(
            Base,
            new
            {
                title = "Intro to Screen Printing",
                runs = new[]
                {
                    new
                    {
                        capacity = 10,
                        sku = "CLS-1-1",
                        startingPriceCents = 30000,
                        instructorId = "77777777-7777-7777-7777-777777777777",
                        sessions = new[] { new { startsAt = "2026-10-24T19:00:00+00:00", endsAt = "2026-10-24T22:00:00+00:00" } },
                    },
                },
            },
            Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"{Base}/{RecordingClassesFacade.Offering.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(RecordingClassesFacade.Offering.ETag, response.Headers.ETag?.ToString());
        Assert.Equal("Intro to Screen Printing", facade.Created!.Title);
        var run = Assert.Single(facade.Created.Runs!);
        Assert.Equal((30000L, "CLS-1-1", 10), (run.StartingPriceCents!.Value, run.Sku, run.Capacity!.Value));
        Assert.Equal(Guid.Parse("77777777-7777-7777-7777-777777777777"), run.InstructorId);
        Assert.Single(run.Sessions!);
        Assert.Equal(Subject, facade.Actor);
    }

    [Fact]
    public async Task ACreateWithoutATitleIsAValidationFailureThatNeverReachesTheFacade()
    {
        var facade = new RecordingClassesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(Base, new { level = "Beginner" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(response));
        Assert.Contains("title", await ErrorFieldsAsync(response));
        Assert.Null(facade.Created);
    }

    [Fact]
    public async Task WhatSquareOwnsIsNotAnEditableField()
    {
        // square.md, ADR 0032: price, seats and sold-out are projections. An unknown member is ignored rather than
        // honoured, so a client sending them changes nothing.
        var facade = new RecordingClassesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(
            client,
            $"{Base}/{RecordingClassesFacade.Offering.Id}",
            """{"priceCents":1,"seatsRemaining":2,"soldOut":true,"status":"Published","level":"Beginner"}""",
            "\"etag\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ClassUpdateViewModel(Level: "Beginner"), facade.Updated);
    }

    [Fact]
    public async Task APatchCarriesItsIfMatchToTheFacadeAndAnswersWithTheNewETag()
    {
        var facade = new RecordingClassesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(
            client, $"{Base}/{RecordingClassesFacade.Offering.Id}", """{"removeHero":true}""", "\"abc\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"abc\"", facade.IfMatch);
        Assert.Equal(new ClassUpdateViewModel(RemoveHero: true), facade.Updated);
        Assert.Equal(RecordingClassesFacade.Offering.ETag, response.Headers.ETag?.ToString());
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
        var facade = new RecordingClassesFacade { Error = OperationError.Of(code, "Refused.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var patch = await PatchAsync(
            client, $"{Base}/{RecordingClassesFacade.Offering.Id}", """{"level":"x"}""", "\"stale\"");
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
        var facade = new RecordingClassesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Command(command, "\"v1\""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(command, facade.Command);
        Assert.Equal("\"v1\"", facade.IfMatch);
        Assert.Equal(RecordingClassesFacade.Offering.ETag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task ScheduleCarriesItsTimeAndIfMatch()
    {
        var facade = new RecordingClassesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        var when = new DateTimeOffset(2026, 12, 24, 17, 0, 0, TimeSpan.Zero);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{RecordingClassesFacade.Offering.Id}/schedule")
        {
            Content = JsonContent.Create(new { scheduledFor = when }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("schedule", "\"v1\""), (facade.Command, facade.IfMatch));
        Assert.Equal(new ContentScheduleViewModel(when), facade.Schedule);
    }

    [Fact]
    public async Task ArchiveIsForAdminsOnly()
    {
        var facade = new RecordingClassesFacade();
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
        using var api = Factory(new RecordingClassesFacade());

        using var member = Client(api, "Member");
        using var forbidden = await member.GetAsync(Base, Ct);
        using var anonymous = api.CreateJsonClient();
        using var unauthenticated = await anonymous.GetAsync(Base, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task AClassIsWrittenWithTheContractsTimesAndMoney()
    {
        var facade = new RecordingClassesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync($"{Base}/{RecordingClassesFacade.Offering.Id}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // api-contract.md: "+00:00", never "Z"; integer cents with a currency; a session's display zone.
        Assert.Contains("\"updatedAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
        Assert.Contains("\"startingPriceCents\":30000", body, StringComparison.Ordinal);
        Assert.Contains("\"currency\":\"USD\"", body, StringComparison.Ordinal);
        Assert.Contains("\"displayTimeZone\":\"America/Chicago\"", body, StringComparison.Ordinal);
        Assert.Contains("\"squareHasRecord\":false", body, StringComparison.Ordinal);
        Assert.Contains("\"instructor\":{\"id\":\"77777777-7777-7777-7777-777777777777\",\"name\":\"Gwen Schilling\"", body, StringComparison.Ordinal);
        Assert.Equal(RecordingClassesFacade.Offering.ETag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task TheListPassesItsQueryThroughAndAnswersAPage()
    {
        var facade = new RecordingClassesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync($"{Base}?limit=5&status=Published&search=print", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ClassListViewModel(null, 5, PublishStatus.Published, "print"), facade.Listed);
    }

    private static HttpRequestMessage Command(string command, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{RecordingClassesFacade.Offering.Id}/{command}");
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

    private static ApiFactory Factory(RecordingClassesFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IClassesFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    /// <summary>The facade as far as the controller is concerned: what it was handed, and one chosen answer.</summary>
    private sealed class RecordingClassesFacade : IClassesFacade
    {
        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<IReadOnlyList<global::Arts6311.Domain.Modules.Classes.Managers.ServiceModels.SiteClassSummaryServiceModel>>> ListForSiteAsync(SiteClassListViewModel request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Modules.Classes.Managers.ServiceModels.SiteClassServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Classes.Managers.ServiceModels.SiteClassServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public static readonly ClassServiceModel Offering = new(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "Intro to Screen Printing",
            "intro-to-screen-printing",
            "A class.",
            null,
            "Printmaking",
            "Beginner",
            OfferingNumber: 1,
            HeroMediaId: null,
            ImageMediaIds: [],
            Runs:
            [
                new ClassRunServiceModel(
                    Guid.Parse("0199f0a0-0000-7000-8000-0000000000c1"),
                    "Fall 2026",
                    new ClassInstructorServiceModel(
                        Guid.Parse("77777777-7777-7777-7777-777777777777"), "Gwen Schilling", "gwen-schilling", PublishStatus.Published),
                    "Saturdays",
                    "America/Chicago",
                    "CLS-1-1",
                    Capacity: 10,
                    StartingPriceCents: 30000,
                    CatalogPolicy.Currency,
                    StartingPriceEditable: true,
                    PriceCents: null,
                    SeatsRemaining: null,
                    SoldOut: false,
                    SquareHasRecord: false,
                    Sessions:
                    [
                        new ClassSessionServiceModel(
                            Guid.Parse("0199f0a0-0000-7000-8000-0000000000c2"),
                            new DateTimeOffset(2026, 10, 24, 19, 0, 0, TimeSpan.Zero),
                            new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero)),
                    ]),
            ],
            PublishStatus.Draft,
            null,
            null,
            SlugLocked: false,
            null,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "editor-1",
            EntityTag.Format(Guid.Parse("0123456789abcdef0123456789abcdef")));

        public OperationError? Error { get; init; }

        public ClassCreateViewModel? Created { get; private set; }

        public ClassUpdateViewModel? Updated { get; private set; }

        public ClassListViewModel? Listed { get; private set; }

        public ContentScheduleViewModel? Schedule { get; private set; }

        public string? Command { get; private set; }

        public string? IfMatch { get; private set; }

        public string? Actor { get; private set; }

        public Task<OperationResult<CursorPage<ClassSummaryServiceModel>>> ListAsync(
            ClassListViewModel request, CancellationToken cancellationToken)
        {
            Listed = request;
            return Task.FromResult(Error is null
                ? OperationResult.Success(new CursorPage<ClassSummaryServiceModel>([], null))
                : OperationResult.Failure<CursorPage<ClassSummaryServiceModel>>(Error));
        }

        public Task<OperationResult<ClassServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) => Answer();

        public Task<OperationResult<ClassServiceModel>> CreateAsync(
            ClassCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
        {
            (Created, Actor) = (create, actorUserId);
            return Answer();
        }

        public Task<OperationResult<ClassServiceModel>> UpdateAsync(
            Guid id, ClassUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            (Updated, IfMatch, Actor) = (update, ifMatch, actorUserId);
            return Answer();
        }

        public Task<OperationResult<ClassServiceModel>> PublishAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("publish", ifMatch, actorUserId);

        public Task<OperationResult<ClassServiceModel>> UnpublishAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unpublish", ifMatch, actorUserId);

        public Task<OperationResult<ClassServiceModel>> ArchiveAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("archive", ifMatch, actorUserId);

        public Task<OperationResult<ClassServiceModel>> ScheduleAsync(
            Guid id,
            ContentScheduleViewModel schedule,
            string? ifMatch,
            string actorUserId,
            CancellationToken cancellationToken)
        {
            Schedule = schedule;
            return Run("schedule", ifMatch, actorUserId);
        }

        public Task<OperationResult<ClassServiceModel>> UnscheduleAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unschedule", ifMatch, actorUserId);

        // The Worker's, not a route's.
        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Task<OperationResult<ClassServiceModel>> Run(string command, string? ifMatch, string actorUserId)
        {
            (Command, IfMatch, Actor) = (command, ifMatch, actorUserId);
            return Answer();
        }

        private Task<OperationResult<ClassServiceModel>> Answer() => Task.FromResult(
            Error is null ? OperationResult.Success(Offering) : OperationResult.Failure<ClassServiceModel>(Error));
    }
}
