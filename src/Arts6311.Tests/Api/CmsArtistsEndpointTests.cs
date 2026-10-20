using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The artist routes at the edge: which token gets in, what reaches the facade, and the statuses and headers that
/// come back. The rules themselves are tested against the real module in <c>Artists/</c>.
/// </summary>
public sealed class CmsArtistsEndpointTests
{
    private const string Base = "/api/v1/cms/artists";
    private const string Subject = "11111111-1111-1111-1111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorCreatesAndGets201WithALocationAndAnETag()
    {
        var facade = new RecordingArtistsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(Base, new { name = "Jane Doe", medium = "Ink", active = false }, Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"{Base}/{RecordingArtistsFacade.Artist.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(RecordingArtistsFacade.Artist.ETag, response.Headers.ETag?.ToString());
        Assert.Equal(new ArtistCreateViewModel("Jane Doe", Medium: "Ink", Active: false), facade.Created);
        Assert.Equal(Subject, facade.Actor);
    }

    [Fact]
    public async Task ACreateWithoutANameIsAValidationFailureThatNeverReachesTheFacade()
    {
        var facade = new RecordingArtistsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(Base, new { medium = "Ink" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(response));
        Assert.Contains("name", await ErrorFieldsAsync(response));
        Assert.Null(facade.Created);
    }

    [Fact]
    public async Task TheStatusIsNotAnEditableField()
    {
        // content.md: a PATCH never changes status. An unknown member is ignored rather than honoured.
        var facade = new RecordingArtistsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(client, $"{Base}/{RecordingArtistsFacade.Artist.Id}", """{"status":"Published","bio":"Hi"}""", "\"etag\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new ArtistUpdateViewModel(Bio: "Hi"), facade.Updated);
    }

    [Fact]
    public async Task APatchCarriesItsIfMatchToTheFacadeAndAnswersWithTheNewETag()
    {
        var facade = new RecordingArtistsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(client, $"{Base}/{RecordingArtistsFacade.Artist.Id}", """{"removePortrait":true}""", "\"abc\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"abc\"", facade.IfMatch);
        Assert.Equal(new ArtistUpdateViewModel(RemovePortrait: true), facade.Updated);
        Assert.Equal(RecordingArtistsFacade.Artist.ETag, response.Headers.ETag?.ToString());
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
        var facade = new RecordingArtistsFacade { Error = OperationError.Of(code, "Refused.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var patch = await PatchAsync(client, $"{Base}/{RecordingArtistsFacade.Artist.Id}", """{"bio":"x"}""", "\"stale\"");
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
        var facade = new RecordingArtistsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.SendAsync(Command(command, "\"v1\""), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(command, facade.Command);
        Assert.Equal("\"v1\"", facade.IfMatch);
        Assert.Equal(RecordingArtistsFacade.Artist.ETag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task ArchiveIsForAdminsOnly()
    {
        var facade = new RecordingArtistsFacade();
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
        using var api = Factory(new RecordingArtistsFacade());

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
        var facade = new RecordingArtistsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var ok = await client.GetAsync($"{Base}?cursor=abc&limit=5&status=Published&search=haase", Ct);
        using var bad = await client.GetAsync($"{Base}?status=Live", Ct);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(new ArtistListViewModel("abc", 5, PublishStatus.Published, "haase"), facade.Listed);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task OneArtistComesWithItsETagStatusByNameAndOffsetTimestamps()
    {
        using var api = Factory(new RecordingArtistsFacade());
        using var client = Client(api, "Editor");

        using var response = await client.GetAsync($"{Base}/{RecordingArtistsFacade.Artist.Id}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(RecordingArtistsFacade.Artist.ETag, response.Headers.ETag?.ToString());
        Assert.Contains("\"status\":\"Draft\"", body, StringComparison.Ordinal);
        Assert.Contains("\"etag\":", body, StringComparison.Ordinal);

        // api-contract.md: "+00:00", never "Z".
        Assert.Contains("\"updatedAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
    }

    private static ApiFactory Factory(RecordingArtistsFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IArtistsFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    [Fact]
    public async Task ScheduleCarriesItsTimeAndIfMatchAndUnscheduleCarriesItsIfMatch()
    {
        var facade = new RecordingArtistsFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        var when = new DateTimeOffset(2026, 12, 24, 17, 0, 0, TimeSpan.Zero);

        using var schedule = new HttpRequestMessage(
            HttpMethod.Post, $"{Base}/{RecordingArtistsFacade.Artist.Id}/schedule")
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
        Assert.Equal(RecordingArtistsFacade.Artist.ETag, unscheduled.Headers.ETag?.ToString());
    }

    private static HttpRequestMessage Command(string command, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{RecordingArtistsFacade.Artist.Id}/{command}");
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

    private sealed class RecordingArtistsFacade : IArtistsFacade
    {
        public static readonly ArtistServiceModel Artist = new(
            Guid.Parse("44444444-4444-4444-4444-444444444444"), "Jane Doe", "jane-doe", null, null, true, null,
            PublishStatus.Draft, null, null, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Subject,
            "\"0123456789abcdef0123456789abcdef\"");

        public OperationError? Error { get; init; }

        public ArtistCreateViewModel? Created { get; private set; }

        public ArtistUpdateViewModel? Updated { get; private set; }

        public ArtistListViewModel? Listed { get; private set; }

        public string? Command { get; private set; }

        public string? IfMatch { get; private set; }

        public string? Actor { get; private set; }

        public Task<OperationResult<CursorPage<ArtistSummaryServiceModel>>> ListAsync(ArtistListViewModel request, CancellationToken cancellationToken)
        {
            Listed = request;
            return Task.FromResult(OperationResult.Success(new CursorPage<ArtistSummaryServiceModel>([], null)));
        }

        public Task<OperationResult<ArtistServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) => Answer();

        // Module-to-module reads: no route reaches them.
        public Task<IReadOnlyList<ArtistSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ArtistSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<ArtistServiceModel>> CreateAsync(ArtistCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
        {
            (Created, Actor) = (create, actorUserId);
            return Answer();
        }

        public Task<OperationResult<ArtistServiceModel>> UpdateAsync(
            Guid id, ArtistUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            (Updated, IfMatch, Actor) = (update, ifMatch, actorUserId);
            return Answer();
        }

        public Task<OperationResult<ArtistServiceModel>> PublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("publish", ifMatch, actorUserId);

        public Task<OperationResult<ArtistServiceModel>> UnpublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unpublish", ifMatch, actorUserId);

        public Task<OperationResult<ArtistServiceModel>> ArchiveAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("archive", ifMatch, actorUserId);

        public ContentScheduleViewModel? Schedule { get; private set; }

        public Task<OperationResult<ArtistServiceModel>> ScheduleAsync(
            Guid id,
            ContentScheduleViewModel schedule,
            string? ifMatch,
            string actorUserId,
            CancellationToken cancellationToken)
        {
            Schedule = schedule;
            return Run("schedule", ifMatch, actorUserId);
        }

        public Task<OperationResult<ArtistServiceModel>> UnscheduleAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unschedule", ifMatch, actorUserId);

        // The Worker's, not a route's.
        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Task<OperationResult<ArtistServiceModel>> Run(string command, string? ifMatch, string actorUserId)
        {
            (Command, IfMatch, Actor) = (command, ifMatch, actorUserId);
            return Answer();
        }

        private Task<OperationResult<ArtistServiceModel>> Answer() => Task.FromResult(
            Error is null ? OperationResult.Success(Artist) : OperationResult.Failure<ArtistServiceModel>(Error));
    }
}
