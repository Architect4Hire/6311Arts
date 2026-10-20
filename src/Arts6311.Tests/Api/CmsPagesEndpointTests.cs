using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The page routes at the edge: which token gets in, what reaches the facade, and the statuses and headers that come
/// back. The rules, and the sanitizer, are tested against the real module in <c>Pages/</c> and <c>Kernel/</c>.
/// </summary>
public sealed class CmsPagesEndpointTests
{
    private const string Base = "/api/v1/cms/pages";
    private const string Subject = "11111111-1111-1111-1111-111111111111";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorCreatesAndGets201WithALocationAndAnETag()
    {
        var facade = new RecordingPagesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(Base, new { title = "Privacy Policy", slug = "privacy", body = "<p>Hi</p>" }, Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"{Base}/{RecordingPagesFacade.Page.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(RecordingPagesFacade.Page.ETag, response.Headers.ETag?.ToString());
        Assert.Equal(new PageCreateViewModel("Privacy Policy", "privacy", "<p>Hi</p>"), facade.Created);
        Assert.Equal(Subject, facade.Actor);
    }

    [Fact]
    public async Task ACreateWithoutATitleNeverReachesTheFacade()
    {
        var facade = new RecordingPagesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsJsonAsync(Base, new { body = "<p>Hi</p>" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("title", await ErrorFieldsAsync(response));
        Assert.Null(facade.Created);
    }

    [Fact]
    public async Task APatchCarriesItsIfMatchAndIgnoresAStatusField()
    {
        var facade = new RecordingPagesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await PatchAsync(client, $"{Base}/{RecordingPagesFacade.Page.Id}", """{"status":"Published","body":""}""", "\"abc\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"abc\"", facade.IfMatch);
        Assert.Equal(new PageUpdateViewModel(Body: ""), facade.Updated);
        Assert.Equal(RecordingPagesFacade.Page.ETag, response.Headers.ETag?.ToString());
    }

    [Theory]
    [InlineData(ErrorCodes.ConcurrencyConflict, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.PreconditionRequired, (HttpStatusCode)428)]
    [InlineData(ContentErrorCodes.SlugTaken, HttpStatusCode.Conflict)]
    [InlineData(ContentErrorCodes.SlugLocked, HttpStatusCode.Conflict)]
    [InlineData(ContentErrorCodes.StatusTransitionInvalid, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest)]
    [InlineData(ErrorCodes.NotFound, HttpStatusCode.NotFound)]
    public async Task AFacadeRefusalAnswersWithItsCodesStatusAndNoETag(string code, HttpStatusCode status)
    {
        var facade = new RecordingPagesFacade { Error = OperationError.Of(code, "Refused.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var patch = await PatchAsync(client, $"{Base}/{RecordingPagesFacade.Page.Id}", """{"body":"x"}""", "\"stale\"");
        using var publish = await client.SendAsync(Command("publish", "\"stale\""), Ct);

        foreach (var response in new[] { patch, publish })
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await CodeAsync(response));
            Assert.Null(response.Headers.ETag);
        }
    }

    [Fact]
    public async Task ArchiveIsForAdminsOnlyAndTheOtherCommandsForEditors()
    {
        var facade = new RecordingPagesFacade();
        using var api = Factory(facade);

        using (var editor = Client(api, "Editor"))
        {
            using var unpublish = await editor.SendAsync(Command("unpublish", "\"v1\""), Ct);
            Assert.Equal(HttpStatusCode.OK, unpublish.StatusCode);
            using var refused = await editor.SendAsync(Command("archive", "\"v1\""), Ct);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("unpublish", facade.Command);
        }

        using var admin = Client(api, "Admin");
        using var allowed = await admin.SendAsync(Command("archive", "\"v1\""), Ct);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal("archive", facade.Command);
    }

    [Fact]
    public async Task AMemberAndAnAnonymousCallerAreKeptOut()
    {
        using var api = Factory(new RecordingPagesFacade());

        using var member = Client(api, "Member");
        using var forbidden = await member.GetAsync(Base, Ct);
        using var anonymous = api.CreateJsonClient();
        using var unauthenticated = await anonymous.GetAsync(Base, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task TheListBindsItsQueryAndOnePageComesWithItsBodyAndOffsetTimestamps()
    {
        var facade = new RecordingPagesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var list = await client.GetAsync($"{Base}?cursor=abc&limit=5&status=Draft&search=priv", Ct);
        using var one = await client.GetAsync($"{Base}/{RecordingPagesFacade.Page.Id}", Ct);
        var body = await one.Content.ReadAsStringAsync(Ct);

        Assert.Equal(new PageListViewModel("abc", 5, PublishStatus.Draft, "priv"), facade.Listed);
        Assert.Equal(RecordingPagesFacade.Page.ETag, one.Headers.ETag?.ToString());
        using (var document = JsonDocument.Parse(body))
        {
            Assert.Equal("<p>Hi</p>", document.RootElement.GetProperty("body").GetString());
        }

        Assert.Contains("\"imageMediaIds\":[]", body, StringComparison.Ordinal);
        Assert.Contains("\"updatedAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
    }

    private static ApiFactory Factory(RecordingPagesFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IPagesFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    [Fact]
    public async Task ScheduleCarriesItsTimeAndIfMatchAndUnscheduleCarriesItsIfMatch()
    {
        var facade = new RecordingPagesFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        var when = new DateTimeOffset(2026, 12, 24, 17, 0, 0, TimeSpan.Zero);

        using var schedule = new HttpRequestMessage(
            HttpMethod.Post, $"{Base}/{RecordingPagesFacade.Page.Id}/schedule")
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
        Assert.Equal(RecordingPagesFacade.Page.ETag, unscheduled.Headers.ETag?.ToString());
    }

    private static HttpRequestMessage Command(string command, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{RecordingPagesFacade.Page.Id}/{command}");
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

    private sealed class RecordingPagesFacade : IPagesFacade
    {
        public static readonly PageServiceModel Page = new(
            Guid.Parse("77777777-7777-7777-7777-777777777777"), "Privacy Policy", "privacy", "<p>Hi</p>", [],
            PublishStatus.Draft, null, null, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, Subject,
            "\"0123456789abcdef0123456789abcdef\"");

        public OperationError? Error { get; init; }

        public PageCreateViewModel? Created { get; private set; }

        public PageUpdateViewModel? Updated { get; private set; }

        public PageListViewModel? Listed { get; private set; }

        public string? Command { get; private set; }

        public string? IfMatch { get; private set; }

        public string? Actor { get; private set; }

        public Task<OperationResult<CursorPage<PageSummaryServiceModel>>> ListAsync(PageListViewModel request, CancellationToken cancellationToken)
        {
            Listed = request;
            return Task.FromResult(OperationResult.Success(new CursorPage<PageSummaryServiceModel>([], null)));
        }

        public Task<OperationResult<PageServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) => Answer();

        public Task<OperationResult<PageServiceModel>> CreateAsync(PageCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
        {
            (Created, Actor) = (create, actorUserId);
            return Answer();
        }

        public Task<OperationResult<PageServiceModel>> UpdateAsync(
            Guid id, PageUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            (Updated, IfMatch, Actor) = (update, ifMatch, actorUserId);
            return Answer();
        }

        public Task<OperationResult<PageServiceModel>> PublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("publish", ifMatch, actorUserId);

        public Task<OperationResult<PageServiceModel>> UnpublishAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unpublish", ifMatch, actorUserId);

        public Task<OperationResult<PageServiceModel>> ArchiveAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("archive", ifMatch, actorUserId);

        public ContentScheduleViewModel? Schedule { get; private set; }

        public Task<OperationResult<PageServiceModel>> ScheduleAsync(
            Guid id,
            ContentScheduleViewModel schedule,
            string? ifMatch,
            string actorUserId,
            CancellationToken cancellationToken)
        {
            Schedule = schedule;
            return Run("schedule", ifMatch, actorUserId);
        }

        public Task<OperationResult<PageServiceModel>> UnscheduleAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            Run("unschedule", ifMatch, actorUserId);

        // The Worker's, not a route's.
        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Task<OperationResult<PageServiceModel>> Run(string command, string? ifMatch, string actorUserId)
        {
            (Command, IfMatch, Actor) = (command, ifMatch, actorUserId);
            return Answer();
        }

        private Task<OperationResult<PageServiceModel>> Answer() => Task.FromResult(
            Error is null ? OperationResult.Success(Page) : OperationResult.Failure<PageServiceModel>(Error));
    }
}
