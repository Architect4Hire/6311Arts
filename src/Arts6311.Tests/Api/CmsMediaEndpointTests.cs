using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The media routes at the edge: which token gets in, how the streamed multipart body reaches the facade, and the
/// statuses and headers that come back. The rules themselves are tested against the real module in <c>Media/</c>.
/// </summary>
public sealed class CmsMediaEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorUploadsAndGets201WithALocationAndAnETag()
    {
        var facade = new RecordingMediaFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsync("/api/v1/cms/media", Upload("photo.jpg", altText: "A gallery wall", preset: "Smallest"), Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal($"/api/v1/cms/media/{RecordingMediaFacade.Asset.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(RecordingMediaFacade.Asset.ETag, response.Headers.ETag?.ToString());

        // The text fields arrived, the file streamed through whole, and the actor is the token's subject.
        var upload = facade.Upload!;
        Assert.Equal("photo.jpg", upload.FileName);
        Assert.Equal("A gallery wall", upload.AltText);
        Assert.Equal(MediaCompressionPreset.Smallest, upload.CompressionPreset);
        Assert.Equal(FileBytes, facade.UploadedBytes);
        Assert.Equal("11111111-1111-1111-1111-111111111111", facade.Actor);
    }

    [Fact]
    public async Task TimestampsAreWrittenWithAnExplicitOffset()
    {
        // api-contract.md: "+00:00", never "Z".
        using var api = Factory(new RecordingMediaFacade());
        using var client = Client(api, "Editor");

        var body = await client.GetStringAsync($"/api/v1/cms/media/{Guid.NewGuid()}", Ct);

        Assert.Contains("\"uploadedAt\":\"1970-01-01T00:00:00+00:00\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownPresetIsRefusedOnItsOwnField()
    {
        var facade = new RecordingMediaFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Editor");

        using var response = await client.PostAsync("/api/v1/cms/media", Upload("photo.jpg", preset: "highquality"), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("compressionPreset", await ErrorFieldsAsync(response));
        Assert.Null(facade.Upload);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("Unknown")]
    public async Task APresetMustBeAName(string preset)
    {
        using var api = Factory(new RecordingMediaFacade());
        using var client = Client(api, "Editor");

        using var response = await client.PostAsync("/api/v1/cms/media", Upload("photo.jpg", preset: preset), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnOverlongTextFieldIsRefusedOnItsOwnField()
    {
        using var api = Factory(new RecordingMediaFacade());
        using var client = Client(api, "Editor");

        using var response = await client.PostAsync("/api/v1/cms/media", Upload("photo.jpg", altText: new string('a', 5000)), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["altText"], await ErrorFieldsAsync(response));
    }

    [Fact]
    public async Task AMemberCannotUpload()
    {
        var facade = new RecordingMediaFacade();
        using var api = Factory(facade);
        using var client = Client(api, "Member");

        using var response = await client.PostAsync("/api/v1/cms/media", Upload("photo.jpg"), Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(facade.Upload);
    }

    [Fact]
    public async Task NoTokenIsA401()
    {
        using var api = Factory(new RecordingMediaFacade());
        using var client = api.CreateJsonClient();

        using var response = await client.GetAsync("/api/v1/cms/media", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNotMultipartIsA415()
    {
        using var api = Factory(new RecordingMediaFacade());
        using var client = Client(api, "Editor");

        using var response = await client.PostAsync(
            "/api/v1/cms/media", new ByteArrayContent(FileBytes) { Headers = { ContentType = new("image/jpeg") } }, Ct);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task AMultipartBodyWithoutAFileIsAValidationFailure()
    {
        using var api = Factory(new RecordingMediaFacade());
        using var client = Client(api, "Editor");
        using var body = new MultipartFormDataContent { { new StringContent("just text"), "altText" } };

        using var response = await client.PostAsync("/api/v1/cms/media", body, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await CodeAsync(response));
    }

    [Theory]
    [InlineData(MediaErrorCodes.TooLarge, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(MediaErrorCodes.TypeUnsupported, HttpStatusCode.UnsupportedMediaType)]
    [InlineData(MediaErrorCodes.ExtensionMismatch, HttpStatusCode.UnsupportedMediaType)]
    [InlineData(MediaErrorCodes.Duplicate, HttpStatusCode.Conflict)]
    [InlineData(MediaErrorCodes.DimensionsOutOfRange, HttpStatusCode.BadRequest)]
    public async Task ARefusedUploadAnswersWithItsCodesStatus(string code, HttpStatusCode status)
    {
        using var api = Factory(new RecordingMediaFacade { Error = OperationError.Of(code, "Refused.") });
        using var client = Client(api, "Editor");

        using var response = await client.PostAsync("/api/v1/cms/media", Upload("photo.jpg"), Ct);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
    }

    [Fact]
    public async Task APatchPassesIfMatchThroughAndAMissingOneIsA428()
    {
        var facade = new RecordingMediaFacade { Error = OperationError.Of(ErrorCodes.PreconditionRequired, "Send If-Match.") };
        using var api = Factory(facade);
        using var client = Client(api, "Editor");
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/cms/media/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { altText = "A wall" }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"abc\"");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Equal("\"abc\"", facade.IfMatch);
        Assert.Equal("A wall", facade.Update?.AltText);
    }

    [Fact]
    public async Task AnEditorCannotDeleteButAnAdminCan()
    {
        var facade = new RecordingMediaFacade();
        using var api = Factory(facade);
        using var editor = Client(api, "Editor");
        using var admin = Client(api, "Admin");
        var id = Guid.NewGuid();

        using var refused = await editor.DeleteAsync($"/api/v1/cms/media/{id}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Null(facade.Deleted);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/cms/media/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", RecordingMediaFacade.Asset.ETag);
        using var deleted = await admin.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(id, facade.Deleted);
    }

    [Fact]
    public async Task AnAnonymousImageRequestIsAPublicViewer()
    {
        var facade = new RecordingMediaFacade();
        using var api = Factory(facade);
        using var client = api.CreateClient();

        using var response = await client.GetAsync($"/media/{Guid.NewGuid()}/800.webp?v=abc", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaViewer.Public, facade.Viewer);
        Assert.Equal("abc", facade.Delivery?.Version);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Equal("\"rendition-etag\"", response.Headers.ETag?.ToString());
        Assert.Equal(FileBytes, await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Theory]
    [InlineData("Editor", MediaViewer.Staff)]
    [InlineData("Admin", MediaViewer.Staff)]
    [InlineData("Member", MediaViewer.Public)]
    public async Task OnlyCmsStaffAreStaffViewers(string role, MediaViewer viewer)
    {
        var facade = new RecordingMediaFacade();
        using var api = Factory(facade);
        using var client = Client(api, role);

        using var response = await client.GetAsync($"/media/{Guid.NewGuid()}/800.webp", Ct);

        Assert.Equal(viewer, facade.Viewer);
    }

    [Fact]
    public async Task AMatchingIfNoneMatchIsA304WithNoBody()
    {
        var facade = new RecordingMediaFacade { NotModified = true };
        using var api = Factory(facade);
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/media/{Guid.NewGuid()}/400.jpg");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"rendition-etag\"");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Equal("\"rendition-etag\"", facade.Delivery?.IfNoneMatch);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task AMissingImageIsANotFoundThatIsNeverStored()
    {
        using var api = Factory(new RecordingMediaFacade { Error = OperationError.Of(ErrorCodes.NotFound, "There is no such image.") });
        using var client = api.CreateJsonClient();

        using var response = await client.GetAsync($"/media/{Guid.NewGuid()}/400.jpg", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static readonly byte[] FileBytes = [.. Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251))];

    private static ApiFactory Factory(RecordingMediaFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IMediaFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    private static MultipartFormDataContent Upload(string fileName, string? altText = null, string? preset = null)
    {
        var body = new MultipartFormDataContent();
        if (altText is not null)
        {
            body.Add(new StringContent(altText), "altText");
        }

        if (preset is not null)
        {
            body.Add(new StringContent(preset), "compressionPreset");
        }

        var file = new ByteArrayContent(FileBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        body.Add(file, "file", fileName);
        return body;
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

    private sealed class RecordingMediaFacade : IMediaFacade
    {
        public static readonly MediaAssetServiceModel Asset = new(
            Guid.Parse("33333333-3333-3333-3333-333333333333"), "photo.jpg", "image/jpeg", 100, 200, 10, 10, new string('a', 64),
            MediaProcessingStatus.Pending, MediaVisibility.Draft, MediaCompressionPreset.Balanced, null, null, "user",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "\"0123456789abcdef0123456789abcdef\"", "/media/x/original?v=a", []);

        public OperationError? Error { get; init; }

        public bool NotModified { get; init; }

        public MediaUploadViewModel? Upload { get; private set; }

        public byte[]? UploadedBytes { get; private set; }

        public string? Actor { get; private set; }

        public MediaAssetUpdateViewModel? Update { get; private set; }

        public string? IfMatch { get; private set; }

        public Guid? Deleted { get; private set; }

        public MediaDeliveryViewModel? Delivery { get; private set; }

        public MediaViewer? Viewer { get; private set; }

        public async Task<OperationResult<MediaAssetServiceModel>> UploadAsync(
            MediaUploadViewModel upload, string actorUserId, CancellationToken cancellationToken)
        {
            Upload = upload;
            Actor = actorUserId;
            using var copy = new MemoryStream();
            await upload.Content.CopyToAsync(copy, cancellationToken);
            UploadedBytes = copy.ToArray();
            return Result(Asset);
        }

        public Task<OperationResult<CursorPage<MediaAssetSummaryServiceModel>>> ListAsync(
            MediaListViewModel request, CancellationToken cancellationToken) =>
            Task.FromResult(Result(new CursorPage<MediaAssetSummaryServiceModel>([], null)));

        public Task<OperationResult<MediaAssetServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Result(Asset));

        public Task<OperationResult<MediaAssetServiceModel>> UpdateAsync(
            Guid id, MediaAssetUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            Update = update;
            IfMatch = ifMatch;
            return Task.FromResult(Result(Asset));
        }

        public Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            Deleted = id;
            return Task.FromResult(Error is { } error ? OperationResult.Failure(error) : OperationResult.Success());
        }

        public Task<OperationResult<MediaContentServiceModel>> OpenAsync(
            MediaDeliveryViewModel request, MediaViewer viewer, CancellationToken cancellationToken)
        {
            Delivery = request;
            Viewer = viewer;
            return Task.FromResult(Result(new MediaContentServiceModel(
                NotModified ? null : new MemoryStream(FileBytes),
                "image/webp",
                FileBytes.Length,
                "\"rendition-etag\"",
                MediaCacheability.Immutable,
                NotModified)));
        }

        public Task<MediaAttachabilityServiceModel> CheckAttachableAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaAttachabilityServiceModel([], []));

        public Task<OperationResult> ProduceRenditionsAsync(Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success());

        public Task PurgeAsync(Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken) => Task.CompletedTask;

        private OperationResult<T> Result<T>(T value) =>
            Error is { } error ? OperationResult.Failure<T>(error) : OperationResult.Success(value);
    }
}
