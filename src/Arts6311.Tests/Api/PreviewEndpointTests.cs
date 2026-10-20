using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Preview.Facade;
using Arts6311.Domain.Modules.Preview.Managers.ServiceModels;
using Arts6311.Domain.Modules.Preview.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Tests.Api;

/// <summary>
/// The preview routes at the edge (ADR 0036): staff mint a link, anyone holding it can read that one draft, and
/// an unusable token is a 404. The tokens themselves are tested against the real module in <c>Preview/</c>.
/// </summary>
public sealed class PreviewEndpointTests
{
    private const string Cms = "/api/v1/cms/preview";
    private const string Site = "/api/v1/site/preview";

    private static readonly Guid EntityId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEditorMintsALinkAndAnAnonymousVisitorReadsItWithoutCaching()
    {
        var facade = new RecordingPreviewFacade();
        using var api = Factory(facade);
        using var editor = Client(api, "Editor");

        using var minted = await editor.PostAsJsonAsync(Cms, new { entityType = "artist", entityId = EntityId }, Ct);
        var link = await minted.Content.ReadFromJsonAsync<PreviewLinkServiceModel>(Ct);

        using var anonymous = api.CreateJsonClient();
        using var read = await anonymous.GetAsync($"{Site}/{link!.Token}", Ct);
        var preview = await read.Content.ReadFromJsonAsync<ContentPreviewServiceModel>(Ct);

        Assert.Equal(HttpStatusCode.OK, minted.StatusCode);
        Assert.Equal(new PreviewLinkRequestViewModel("artist", EntityId), facade.Requested);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(RecordingPreviewFacade.Token, facade.ReadToken);
        Assert.True(preview!.NoIndex);

        // A draft must not be kept by this host or anything in front of it, and must not be indexed.
        Assert.Equal("no-store", read.Headers.CacheControl?.ToString());
        Assert.Equal("noindex, nofollow", read.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task AnExpiredOrUnusableTokenIs404()
    {
        using var api = Factory(new RecordingPreviewFacade
        {
            Error = OperationError.Of(ErrorCodes.NotFound, "This preview link is not valid any more."),
        });
        using var anonymous = api.CreateJsonClient();

        using var response = await anonymous.GetAsync($"{Site}/{RecordingPreviewFacade.Token}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(ErrorCodes.NotFound, document.RootElement.GetProperty("code").GetString());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task MintingALinkNeedsAStaffSessionAndAMemberCannotDoIt()
    {
        var facade = new RecordingPreviewFacade();
        using var api = Factory(facade);
        using var anonymous = api.CreateJsonClient();
        using var member = Client(api, "Member");

        using var withoutASession = await anonymous.PostAsJsonAsync(Cms, new { entityType = "artist", entityId = EntityId }, Ct);
        using var asMember = await member.PostAsJsonAsync(Cms, new { entityType = "artist", entityId = EntityId }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, withoutASession.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asMember.StatusCode);
        Assert.Null(facade.Requested);
    }

    [Fact]
    public async Task APreviewTokenIsNoCredentialForAnythingElse()
    {
        // The restriction in 2.8: a preview grants no write access. The token is not a bearer token, so a write
        // carrying it is unauthenticated.
        using var api = Factory(new RecordingPreviewFacade());
        using var holder = api.CreateJsonClient();
        holder.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RecordingPreviewFacade.Token);

        using var mint = await holder.PostAsJsonAsync(Cms, new { entityType = "artist", entityId = EntityId }, Ct);
        using var publish = await holder.PostAsync($"/api/v1/cms/artists/{EntityId}/publish", content: null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, mint.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, publish.StatusCode);
    }

    [Fact]
    public async Task ARefusedRequestForALinkAnswersWithItsCodesStatus()
    {
        using var api = Factory(new RecordingPreviewFacade
        {
            Error = OperationError.Validation("No.", [("entityType", "Not previewable.")]),
        });
        using var editor = Client(api, "Editor");

        using var response = await editor.PostAsJsonAsync(Cms, new { entityType = "redirect", entityId = EntityId }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static ApiFactory Factory(RecordingPreviewFacade facade) =>
        new(configureServices: services => services.Replace(ServiceDescriptor.Scoped<IPreviewFacade>(_ => facade)));

    private static HttpClient Client(ApiFactory api, string role)
    {
        var client = api.CreateJsonClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Tokens.UserToken(role));
        return client;
    }

    private sealed class RecordingPreviewFacade : IPreviewFacade
    {
        public const string Token = "CfDJ8-a-token";

        public OperationError? Error { get; init; }

        public PreviewLinkRequestViewModel? Requested { get; private set; }

        public string? ReadToken { get; private set; }

        public Task<OperationResult<PreviewLinkServiceModel>> CreateLinkAsync(
            PreviewLinkRequestViewModel request, CancellationToken cancellationToken)
        {
            Requested = request;
            return Task.FromResult(Error is null
                ? OperationResult.Success(new PreviewLinkServiceModel(Token, $"/preview/{Token}", DateTimeOffset.UnixEpoch))
                : OperationResult.Failure<PreviewLinkServiceModel>(Error));
        }

        public Task<OperationResult<ContentPreviewServiceModel>> ReadAsync(string? token, CancellationToken cancellationToken)
        {
            ReadToken = token;
            return Task.FromResult(Error is null
                ? OperationResult.Success(new ContentPreviewServiceModel(
                    "artist", EntityId, "/artists/jane-doe", PublishStatus.Draft, NoIndex: true))
                : OperationResult.Failure<ContentPreviewServiceModel>(Error));
        }
    }
}
