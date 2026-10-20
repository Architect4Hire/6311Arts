using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Redirects.Facade;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The redirect routes at the edge: the CMS's are Admin-only, the site's is anonymous and cacheable, and refusals
/// answer with their codes' statuses. The rules are tested against the real module in <c>Redirects/</c>.
/// </summary>
public sealed class RedirectsEndpointTests
{
    private const string Cms = "/api/v1/cms/redirects";
    private const string Site = "/api/v1/site/redirects";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyAnAdminReachesTheCmsRoutes()
    {
        var facade = new RecordingRedirectsFacade();
        using var api = Factory(facade);

        using (var editor = Client(api, "Editor"))
        {
            using var list = await editor.GetAsync(Cms, Ct);
            using var create = await editor.PostAsJsonAsync(Cms, new { fromPath = "/a", toPath = "/b" }, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
            Assert.Null(facade.Created);
        }

        using var admin = Client(api, "Admin");
        using var created = await admin.PostAsJsonAsync(Cms, new { fromPath = "/a", toPath = "/b", statusCode = 302 }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"{Cms}/{RecordingRedirectsFacade.Redirect.Id}", created.Headers.Location?.OriginalString);
        Assert.Equal(RecordingRedirectsFacade.Redirect.ETag, created.Headers.ETag?.ToString());
        Assert.Equal(new RedirectCreateViewModel("/a", "/b", 302), facade.Created);
    }

    [Theory]
    [InlineData(RedirectErrorCodes.LoopOrChain, HttpStatusCode.Conflict)]
    [InlineData(RedirectErrorCodes.FromTaken, HttpStatusCode.Conflict)]
    [InlineData(ErrorCodes.ValidationFailed, HttpStatusCode.BadRequest)]
    public async Task ARefusalAnswersWithItsCodesStatus(string code, HttpStatusCode status)
    {
        using var api = Factory(new RecordingRedirectsFacade { Error = OperationError.Of(code, "Refused.") });
        using var admin = Client(api, "Admin");

        using var response = await admin.PostAsJsonAsync(Cms, new { fromPath = "/a", toPath = "/b" }, Ct);

        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(code, document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ADeleteCarriesItsIfMatchAndAnswers204()
    {
        var facade = new RecordingRedirectsFacade();
        using var api = Factory(facade);
        using var admin = Client(api, "Admin");

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Cms}/{RecordingRedirectsFacade.Redirect.Id}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"v1\"");
        using var response = await admin.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("\"v1\"", facade.IfMatch);
    }

    [Fact]
    public async Task TheSiteListIsAnonymousCacheableAndNamesNoOneWhoMadeThem()
    {
        using var api = Factory(new RecordingRedirectsFacade());
        using var anonymous = api.CreateJsonClient();

        using var response = await anonymous.GetAsync(Site, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl?.ToString());
        Assert.Equal("""[{"fromPath":"/gallery","toPath":"/exhibitions","statusCode":301}]""", body);
    }

    private static ApiFactory Factory(RecordingRedirectsFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IRedirectsFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    private sealed class RecordingRedirectsFacade : IRedirectsFacade
    {
        public static readonly RedirectServiceModel Redirect = new(
            Guid.Parse("88888888-8888-8888-8888-888888888888"), "/a", "/b", 301, RedirectSource.Manual, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "admin-1", "\"0123456789abcdef0123456789abcdef\"");

        public OperationError? Error { get; init; }

        public RedirectCreateViewModel? Created { get; private set; }

        public string? IfMatch { get; private set; }

        public Task<OperationResult<CursorPage<RedirectServiceModel>>> ListAsync(RedirectListViewModel request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success(new CursorPage<RedirectServiceModel>([Redirect], null)));

        public Task<OperationResult<IReadOnlyList<SiteRedirectServiceModel>>> ListForSiteAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Success<IReadOnlyList<SiteRedirectServiceModel>>([new SiteRedirectServiceModel("/gallery", "/exhibitions", 301)]));

        public Task<OperationResult<RedirectServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) => Answer();

        public Task<OperationResult<RedirectServiceModel>> CreateAsync(RedirectCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
        {
            Created = create;
            return Answer();
        }

        public Task<OperationResult<RedirectServiceModel>> UpdateAsync(
            Guid id, RedirectUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            IfMatch = ifMatch;
            return Answer();
        }

        public Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
        {
            IfMatch = ifMatch;
            return Task.FromResult(Error is null ? OperationResult.Success() : OperationResult.Failure(Error));
        }

        public Task<OperationResult> ApplySlugChangeAsync(RedirectSlugChangeViewModel change, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Task<OperationResult<RedirectServiceModel>> Answer() => Task.FromResult(
            Error is null ? OperationResult.Success(Redirect) : OperationResult.Failure<RedirectServiceModel>(Error));
    }
}
