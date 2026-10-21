using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Forms.Facade;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.ServiceModels;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The forms routes at the edge (ADR 0047): the public ones are anonymous and answer 202 with nothing more, a
/// submission is parsed as streamed multipart, and the CMS's are an editor's — export and removal an Admin's. The rules
/// themselves are tested against the real module in <c>Forms/</c>.
/// </summary>
public sealed class FormsEndpointTests
{
    private const string Public = "/api/v1/forms";
    private const string Cms = "/api/v1/cms/forms";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSignUpAndContactFormsAreAnonymousAndAnswer202WithNoBody()
    {
        var facade = new RecordingFormsFacade();
        using var api = Factory(facade);
        using var anonymous = api.CreateJsonClient();

        using var signup = await anonymous.PostAsJsonAsync($"{Public}/newsletter", new { email = "ana@example.org", company = "" }, Ct);
        using var contact = await anonymous.PostAsJsonAsync($"{Public}/contact", new { name = "Sam", email = "sam@example.org", message = "Hi" }, Ct);

        Assert.Equal(HttpStatusCode.Accepted, signup.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, contact.StatusCode);
        Assert.Empty(await signup.Content.ReadAsStringAsync(Ct));
        Assert.Equal("ana@example.org", facade.Signup!.Email);
        Assert.Equal("Hi", facade.Contact!.Message);
    }

    [Fact]
    public async Task ARefusedFormIsA400WithItsFieldErrors()
    {
        using var api = Factory(new RecordingFormsFacade
        {
            Error = OperationError.Validation("One or more fields are not valid.", [("email", "That email address does not look right.")]),
        });
        using var anonymous = api.CreateJsonClient();

        using var response = await anonymous.PostAsJsonAsync($"{Public}/newsletter", new { email = "nope" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task ASubmissionsAnswersComeFirstAndItsPicturesStreamToTheFacade()
    {
        var facade = new RecordingFormsFacade();
        using var api = Factory(facade);
        using var anonymous = api.CreateJsonClient();

        using var body = Multipart(
            ("submission", null, Json(FormsSamplesJson)),
            ("images", "lake.jpg", [1, 2, 3]),
            ("note", null, Encoding.UTF8.GetBytes("ignored")),
            ("images", "print.png", [4, 5]));
        using var response = await anonymous.PostAsync($"{Public}/submissions", body, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Jane Rivera", facade.Submission!.Artist!.Name);
        Assert.Equal(["sell", "teach"], facade.Submission.Wants);
        Assert.Equal([("lake.jpg", 3), ("print.png", 2)], facade.Images);
    }

    [Fact]
    public async Task TheAnswersMayArriveAsABlobWithAFileName()
    {
        // A browser's FormData sends a JSON Blob (the way to give the part its type) with filename="blob"; the part
        // is known by its name, not by whether it has a file name.
        var facade = new RecordingFormsFacade();
        using var api = Factory(facade);
        using var anonymous = api.CreateJsonClient();

        using var body = Multipart(("submission", "blob", Json(FormsSamplesJson)), ("images", "a.jpg", [1]));
        using var response = await anonymous.PostAsync($"{Public}/submissions", body, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Jane Rivera", facade.Submission!.Artist!.Name);
        Assert.Equal([("a.jpg", 1)], facade.Images);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASubmissionWhoseFirstPartIsNotTheAnswersOrIsNotJsonIsRefusedBeforeAnyPictureIsRead(bool notJson)
    {
        var facade = new RecordingFormsFacade();
        using var api = Factory(facade);
        using var anonymous = api.CreateJsonClient();

        using var body = notJson
            ? Multipart(("submission", null, Encoding.UTF8.GetBytes("{not json")), ("images", "a.jpg", [1]))
            : Multipart(("images", "a.jpg", [1]), ("submission", null, Json(FormsSamplesJson)));
        using var response = await anonymous.PostAsync($"{Public}/submissions", body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.True(document.RootElement.GetProperty("errors").TryGetProperty("submission", out _));
        Assert.Null(facade.Submission);
    }

    [Fact]
    public async Task TheInboxIsAnEditorsAndAnonymousCallersAreTurnedAway()
    {
        var facade = new RecordingFormsFacade();
        using var api = Factory(facade);

        using (var anonymous = api.CreateJsonClient())
        {
            using var refused = await anonymous.GetAsync($"{Cms}/submissions", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }

        using var editor = Client(api, "Editor");
        using var list = await editor.GetAsync($"{Cms}/submissions?status=Accepted&limit=5", Ct);
        using var one = await editor.GetAsync($"{Cms}/submissions/{RecordingFormsFacade.Sample.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(new SubmissionListViewModel(null, 5, SubmissionStatus.Accepted), facade.SubmissionList);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(RecordingFormsFacade.Sample.ETag, one.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task AStatusChangeCarriesItsIfMatchAndAnswersWithTheNewETag()
    {
        var facade = new RecordingFormsFacade();
        using var api = Factory(facade);
        using var editor = Client(api, "Editor");

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{Cms}/submissions/{RecordingFormsFacade.Sample.Id}")
        {
            Content = JsonContent.Create(new { status = "Reviewed" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        using var response = await editor.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"v1\"", facade.IfMatch);
        Assert.Equal(SubmissionStatus.Reviewed, facade.SubmissionUpdate!.Status);
        Assert.Equal(RecordingFormsFacade.Sample.ETag, response.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task OnlyAnAdminExportsOrRemovesAnAddress()
    {
        var facade = new RecordingFormsFacade();
        using var api = Factory(facade);
        var id = Guid.NewGuid();

        using (var editor = Client(api, "Editor"))
        {
            using var export = await editor.GetAsync($"{Cms}/newsletter/export", Ct);
            using var remove = await editor.DeleteAsync($"{Cms}/newsletter/{id}", Ct);
            Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, remove.StatusCode);
            Assert.Null(facade.ExportedBy);
        }

        using var admin = Client(api, "Admin");
        using var exported = await admin.GetAsync($"{Cms}/newsletter/export", Ct);
        using var removed = await admin.DeleteAsync($"{Cms}/newsletter/{id}", Ct);

        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.Equal("text/csv", exported.Content.Headers.ContentType?.MediaType);
        Assert.Equal("6311arts-newsletter.csv", exported.Content.Headers.ContentDisposition?.FileName);
        Assert.True(exported.Headers.CacheControl?.NoStore);
        Assert.Equal("\"email\"\r\n", Encoding.UTF8.GetString(await exported.Content.ReadAsByteArrayAsync(Ct))[1..]);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(id, facade.Removed);
    }

    private const string FormsSamplesJson = """
        {
          "artist": { "name": "Jane Rivera", "email": "jane@example.org" },
          "wants": ["sell", "teach"],
          "consent": { "ownWork": true }
        }
        """;

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    private static MultipartFormDataContent Multipart(params (string Name, string? FileName, byte[] Bytes)[] parts)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, fileName, bytes) in parts)
        {
            var part = new ByteArrayContent(bytes);
            if (fileName is null)
            {
                part.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                content.Add(part, name);
            }
            else
            {
                part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                content.Add(part, name, fileName);
            }
        }

        return content;
    }

    private static ApiFactory Factory(RecordingFormsFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IFormsFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    private sealed class RecordingFormsFacade : IFormsFacade
    {
        public static readonly SubmissionServiceModel Sample = new(
            Guid.Parse("018f7c2e-0000-7000-8000-000000000001"), "Jane Rivera", "jane@example.org", ["sell"],
            new SubmissionAnswersServiceModel(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false),
            [], SubmissionStatus.New, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "\"etag-1\"");

        public OperationError? Error { get; init; }

        public ArtistSubmissionViewModel? Submission { get; private set; }

        public List<(string FileName, int Bytes)> Images { get; } = [];

        public ContactMessageViewModel? Contact { get; private set; }

        public NewsletterSignupViewModel? Signup { get; private set; }

        public SubmissionListViewModel? SubmissionList { get; private set; }

        public SubmissionUpdateViewModel? SubmissionUpdate { get; private set; }

        public string? IfMatch { get; private set; }

        public string? ExportedBy { get; private set; }

        public Guid? Removed { get; private set; }

        public async Task<OperationResult> SubmitAsync(
            ArtistSubmissionViewModel submission, IAsyncEnumerable<SubmissionImageViewModel> images, CancellationToken cancellationToken)
        {
            Submission = submission;
            await foreach (var image in images.WithCancellation(cancellationToken))
            {
                using var copy = new MemoryStream();
                await image.Content.CopyToAsync(copy, cancellationToken);
                Images.Add((image.FileName, (int)copy.Length));
            }

            return Result();
        }

        public Task<OperationResult> ContactAsync(ContactMessageViewModel message, CancellationToken cancellationToken)
        {
            Contact = message;
            return Task.FromResult(Result());
        }

        public Task<OperationResult> SubscribeAsync(NewsletterSignupViewModel signup, CancellationToken cancellationToken)
        {
            Signup = signup;
            return Task.FromResult(Result());
        }

        public Task<OperationResult<CursorPage<SubmissionSummaryServiceModel>>> ListSubmissionsAsync(
            SubmissionListViewModel request, CancellationToken cancellationToken)
        {
            SubmissionList = request;
            return Task.FromResult(OperationResult.Success(new CursorPage<SubmissionSummaryServiceModel>([], null)));
        }

        public Task<OperationResult<SubmissionServiceModel>> GetSubmissionAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success(Sample));

        public Task<OperationResult<SubmissionServiceModel>> UpdateSubmissionAsync(
            Guid id, SubmissionUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            SubmissionUpdate = update;
            IfMatch = ifMatch;
            return Task.FromResult(OperationResult.Success(Sample));
        }

        public Task<OperationResult<CursorPage<ContactMessageServiceModel>>> ListContactsAsync(
            ContactListViewModel request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success(new CursorPage<ContactMessageServiceModel>([], null)));

        public Task<OperationResult<ContactMessageServiceModel>> GetContactAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<ContactMessageServiceModel>> UpdateContactAsync(
            Guid id, ContactUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<CursorPage<NewsletterSubscriberServiceModel>>> ListSubscribersAsync(
            NewsletterListViewModel request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success(new CursorPage<NewsletterSubscriberServiceModel>([], null)));

        public Task<OperationResult> RemoveSubscriberAsync(Guid id, string actorUserId, CancellationToken cancellationToken)
        {
            Removed = id;
            return Task.FromResult(OperationResult.Success());
        }

        public Task<NewsletterExportServiceModel> ExportSubscribersAsync(string actorUserId, CancellationToken cancellationToken)
        {
            ExportedBy = actorUserId;
            return Task.FromResult(new NewsletterExportServiceModel("6311arts-newsletter.csv", "\"email\"\r\n", 0));
        }

        private OperationResult Result() => Error is null ? OperationResult.Success() : OperationResult.Failure(Error);
    }
}
