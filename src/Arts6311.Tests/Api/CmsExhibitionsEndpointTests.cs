using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The exhibition routes at the edge: which token gets in, what reaches the facade, and the statuses, headers and
/// wire formats that come back. The rules themselves are tested against the real module in <c>Exhibitions/</c>.
/// </summary>
public sealed class CmsExhibitionsEndpointTests
{
    private const string Base = "/api/v1/cms/exhibitions";
    private const string Subject = "11111111-1111-1111-1111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorCreatesAndGets201WithALocationAndAnETag()
    {
        var facade = new RecordingExhibitionsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        var jane = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            Base,
            new
            {
                title = "Boats & Pianos",
                startDate = "2026-10-24",
                endDate = "2026-12-05",
                reception = new { startsAt = "2026-10-24T18:00:00-05:00", endsAt = "2026-10-24T21:00:00-05:00", note = "Free" },
                artistIds = new[] { jane },
            },
            Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"{Base}/{RecordingExhibitionsFacade.Exhibition.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(RecordingExhibitionsFacade.Exhibition.ETag, response.Headers.ETag?.ToString());

        var created = facade.Created!;
        Assert.Equal(("Boats & Pianos", new DateOnly(2026, 10, 24), new DateOnly(2026, 12, 5)), (created.Title, created.StartDate!.Value, created.EndDate!.Value));
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), created.Reception!.StartsAt);
        Assert.Equal([jane], created.ArtistIds);
        Assert.Equal(Subject, facade.Actor);
    }

    [Fact]
    public async Task ACreateWithoutATitleOrWithAMalformedDateNeverReachesTheFacade()
    {
        // Missing dates are the facade's validator's (ExhibitionValidatorTests); these fail at binding.
        var facade = new RecordingExhibitionsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var missing = await client.PostAsJsonAsync(Base, new { startDate = "2026-10-24", endDate = "2026-12-05" }, Ct);
        using var malformed = await client.PostAsJsonAsync(Base, new { title = "Queue", startDate = "24/10/2026", endDate = "2026-12-05" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("title", await ErrorFieldsAsync(missing));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(malformed));
        Assert.Null(facade.Created);
    }

    [Fact]
    public async Task APatchCarriesItsIfMatchAndIgnoresAStatusField()
    {
        // content.md: a PATCH never changes status. An unknown member is ignored rather than honoured.
        var facade = new RecordingExhibitionsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(
            client, $"{Base}/{RecordingExhibitionsFacade.Exhibition.Id}", """{"status":"Published","artistIds":[],"removeHero":true}""", "\"abc\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"abc\"", facade.IfMatch);
        Assert.Equal(true, facade.Updated!.RemoveHero);
        Assert.Empty(facade.Updated.ArtistIds!);
        Assert.Equal(RecordingExhibitionsFacade.Exhibition.ETag, response.Headers.ETag?.ToString());
    }

    [Theory]
    [InlineData(ErrorCodes.ConcurrencyConflict, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.PreconditionRequired, (HttpStatusCode)428)]
    [InlineData(ContentErrorCodes.SlugTaken, HttpStatusCode.Conflict)]
    [InlineData(ContentErrorCodes.SlugLocked, HttpStatusCode.Conflict)]
    [InlineData(ContentErrorCodes.StatusTransitionInvalid, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.NotFound, HttpStatusCode.NotFound)]
    public async Task AFacadeRefusalAnswersWithItsCodesStatusAndNoETag(string code, HttpStatusCode status)
    {
        var facade = new RecordingExhibitionsFacade { Error = OperationError.Of(code, "Refused.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var patch = await PatchAsync(client, $"{Base}/{RecordingExhibitionsFacade.Exhibition.Id}", """{"body":"x"}""", "\"stale\"");
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
    public async Task AnEditorRunsTheStatusCommands(string command)
    {
        var facade = new RecordingExhibitionsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Command(command, "\"v1\""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((command, "\"v1\""), (facade.Command, facade.IfMatch));
    }

    [Fact]
    public async Task ArchiveIsForAdminsOnly()
    {
        var facade = new RecordingExhibitionsFacade();
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
        using var api = Factory(new RecordingExhibitionsFacade());

        using var member = Client(api, "Member");
        using var forbidden = await member.GetAsync(Base, Ct);
        using var anonymous = api.CreateJsonClient();
        using var unauthenticated = await anonymous.GetAsync(Base, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task TheListBindsItsQueryAndRefusesAnUnknownStatus()
    {
        var facade = new RecordingExhibitionsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var ok = await client.GetAsync($"{Base}?cursor=abc&limit=5&status=Archived&search=boats", Ct);
        using var bad = await client.GetAsync($"{Base}?status=Live", Ct);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(new ExhibitionListViewModel("abc", 5, PublishStatus.Archived, "boats"), facade.Listed);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task OneExhibitionComesWithDatesAsDatesUtcOffsetTimesTheDisplayZoneAndItsCredits()
    {
        using var api = Factory(new RecordingExhibitionsFacade());
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync($"{Base}/{RecordingExhibitionsFacade.Exhibition.Id}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(RecordingExhibitionsFacade.Exhibition.ETag, response.Headers.ETag?.ToString());
        Assert.Contains("\"startDate\":\"2026-10-24\"", body, StringComparison.Ordinal);
        Assert.Contains("\"displayTimeZone\":\"America/Chicago\"", body, StringComparison.Ordinal);
        Assert.Contains("\"artists\":[{\"id\":\"66666666-6666-6666-6666-666666666666\",\"name\":\"Brian Eaton\",\"slug\":\"brian-eaton\",\"status\":\"Published\"}]", body, StringComparison.Ordinal);

        // api-contract.md: "+00:00", never "Z".
        Assert.Contains("\"startsAt\":\"2026-10-24T23:00:00+00:00\"", body, StringComparison.Ordinal);
        Assert.Contains("\"updatedAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
    }

    private static ApiFactory Factory(RecordingExhibitionsFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IExhibitionsFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    [Fact]
    public async Task ScheduleCarriesItsTimeAndIfMatchAndUnscheduleCarriesItsIfMatch()
    {
        var facade = new RecordingExhibitionsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        var when = new DateTimeOffset(2026, 12, 24, 17, 0, 0, TimeSpan.Zero);

        using var schedule = new HttpRequestMessage(
            HttpMethod.Post, $"{Base}/{RecordingExhibitionsFacade.Exhibition.Id}/schedule")
        {
            Content = JsonContent.Create(new { scheduledFor = when }),
        };
        schedule.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        using var scheduled = await client.SendAsync(schedule, Ct);
        var (scheduleCommand, scheduleIfMatch) = (facade.Command, facade.IfMatch);
        using var unscheduled = await client.SendAsync(Command("unschedule", "\"v2\""), Ct);

        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        Assert.Equal(when, facade.Schedule!.ScheduledFor);
        Assert.Equal(("schedule", "\"v1\""), (scheduleCommand, scheduleIfMatch));
        Assert.Equal(HttpStatusCode.OK, unscheduled.StatusCode);
        Assert.Equal("unschedule", facade.Command);
        Assert.Equal("\"v2\"", facade.IfMatch);
        Assert.Equal(RecordingExhibitionsFacade.Exhibition.ETag, unscheduled.Headers.ETag?.ToString());
    }

    private static HttpRequestMessage Command(string command, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{RecordingExhibitionsFacade.Exhibition.Id}/{command}");
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

    private static async Task<List<string>> ErrorFieldsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return [.. document.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name)];
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.GetProperty("code").GetString();
    }

    private sealed class RecordingExhibitionsFacade : IExhibitionsFacade
    {
        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<IReadOnlyList<global::Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels.SiteExhibitionSummaryServiceModel>>> ListForSiteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels.SiteExhibitionServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels.SiteExhibitionServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public static readonly ExhibitionServiceModel Exhibition = new(
            Guid.Parse("55555555-5555-5555-5555-555555555555"), "Boats & Pianos", "boats-pianos", "New work.", null,
            new DateOnly(2026, 10, 24), new DateOnly(2026, 12, 5),
            new ExhibitionReceptionServiceModel(
                new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.Zero), "Free"),
            "America/Chicago", null,
            [new ExhibitionArtistServiceModel(Guid.Parse("66666666-6666-6666-6666-666666666666"), "Brian Eaton", "brian-eaton", PublishStatus.Published)],
            PublishStatus.Draft, null, null, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Subject,
            "\"0123456789abcdef0123456789abcdef\"");

        public OperationError? Error { get; init; }

        public ExhibitionCreateViewModel? Created { get; private set; }

        public ExhibitionUpdateViewModel? Updated { get; private set; }

        public ExhibitionListViewModel? Listed { get; private set; }

        public string? Command { get; private set; }

        public string? IfMatch { get; private set; }

        public string? Actor { get; private set; }

        public Task<OperationResult<CursorPage<ExhibitionSummaryServiceModel>>> ListAsync(
            ExhibitionListViewModel request, CancellationToken cancellationToken)
        {
            Listed = request;
            return Task.FromResult(OperationResult.Success(new CursorPage<ExhibitionSummaryServiceModel>([], null)));
        }

        public Task<OperationResult<ExhibitionServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) => Answer();

        public Task<OperationResult<ExhibitionServiceModel>> CreateAsync(
            ExhibitionCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
        {
            (Created, Actor) = (create, actorUserId);
            return Answer();
        }

        public Task<OperationResult<ExhibitionServiceModel>> UpdateAsync(
            Guid id, ExhibitionUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            (Updated, IfMatch, Actor) = (update, ifMatch, actorUserId);
            return Answer();
        }

        public Task<OperationResult<ExhibitionServiceModel>> PublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("publish", ifMatch, actorUserId);

        public Task<OperationResult<ExhibitionServiceModel>> UnpublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unpublish", ifMatch, actorUserId);

        public Task<OperationResult<ExhibitionServiceModel>> ArchiveAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("archive", ifMatch, actorUserId);

        public ContentScheduleViewModel? Schedule { get; private set; }

        public Task<OperationResult<ExhibitionServiceModel>> ScheduleAsync(
            Guid id,
            ContentScheduleViewModel schedule,
            string? ifMatch,
            string actorUserId,
            CancellationToken cancellationToken)
        {
            Schedule = schedule;
            return Run("schedule", ifMatch, actorUserId);
        }

        public Task<OperationResult<ExhibitionServiceModel>> UnscheduleAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unschedule", ifMatch, actorUserId);

        // The Worker's, not a route's.
        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Task<OperationResult<ExhibitionServiceModel>> Run(string command, string? ifMatch, string actorUserId)
        {
            (Command, IfMatch, Actor) = (command, ifMatch, actorUserId);
            return Answer();
        }

        private Task<OperationResult<ExhibitionServiceModel>> Answer() => Task.FromResult(
            Error is null ? OperationResult.Success(Exhibition) : OperationResult.Failure<ExhibitionServiceModel>(Error));
    }
}
