using System.Net;
using System.Net.Http.Json;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers.ServiceModels;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Api;

/// <summary>
/// The internal session routes (auth.md). They are the only auth routes in the API, and they take a service
/// token and nothing else.
/// </summary>
/// <remarks>
/// The facade is faked: what is under test here is the edge — which token gets in, and what shape comes back.
/// The rules themselves are covered against real Identity in <c>Identity/CredentialCheckTests</c>.
/// </remarks>
public sealed class InternalSessionsEndpointTests
{
    private const string Credentials = "/api/v1/internal/sessions/credentials";
    private const string Validate = "/api/v1/internal/sessions/validate";
    private const string Revoke = "/api/v1/internal/sessions/revoke";

    [Fact]
    public async Task AServiceTokenReachesTheCredentialRoute()
    {
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        using var request = Request(HttpMethod.Post, Credentials, api.Tokens.ServiceToken());
        request.Content = JsonContent.Create(new CredentialCheckViewModel("a@b.test", "secret-enough"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<UserSummaryServiceModel>(
            TestContext.Current.CancellationToken);
        Assert.Equal("user-1", summary!.UserId);
        Assert.Equal(1, facade.CredentialCalls);
    }

    [Fact]
    public async Task NoTokenIs401()
    {
        using var api = Factory(new FakeIdentityFacade());
        using var client = api.CreateJsonClient();

        var response = await client.PostAsJsonAsync(
            Credentials, new CredentialCheckViewModel("a@b.test", "x"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Editor")]
    [InlineData("Member")]
    public async Task AUserTokenIs403EvenForAnAdmin(string role)
    {
        // These routes are for the gateway acting as itself, not for a signed-in person however privileged
        // (ADR 0021). An Admin's token must not reach them.
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        using var request = Request(HttpMethod.Post, Credentials, api.Tokens.UserToken(role));
        request.Content = JsonContent.Create(new CredentialCheckViewModel("a@b.test", "x"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, facade.CredentialCalls);
    }

    [Fact]
    public async Task AServiceTokenReachesTheValidateRoute()
    {
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        using var request = Request(HttpMethod.Post, Validate, api.Tokens.ServiceToken());
        request.Content = JsonContent.Create(new SessionValidationViewModel("user-1", "stamp"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var validation = await response.Content.ReadFromJsonAsync<SessionValidationServiceModel>(
            TestContext.Current.CancellationToken);
        Assert.True(validation!.Valid);
    }

    [Fact]
    public async Task AFailedCheckIsAProblemWithItsStableCode()
    {
        var facade = new FakeIdentityFacade
        {
            Error = OperationError.Of("auth.locked_out", "Locked."),
        };
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        using var request = Request(HttpMethod.Post, Credentials, api.Tokens.ServiceToken());
        request.Content = JsonContent.Create(new CredentialCheckViewModel("a@b.test", "x"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        // An unmapped code is a 400: the gateway is the caller, and a lockout is something it must act on.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("auth.locked_out", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServiceTokenReachesTheRevokeRoute()
    {
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        using var request = Request(HttpMethod.Post, Revoke, api.Tokens.ServiceToken());
        request.Content = JsonContent.Create(new SessionRevocationViewModel("user-1"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, facade.RevokeCalls);
        Assert.Equal("user-1", facade.RevokedUserId);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Member")]
    public async Task AUserTokenCannotRevokeSessions(string role)
    {
        // "Log out everywhere" is the gateway asking on a person's behalf, and the gateway is the only caller.
        // A user token here would let a signed-in person end anyone's sessions by choosing a user id.
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        using var request = Request(HttpMethod.Post, Revoke, api.Tokens.UserToken(role));
        request.Content = JsonContent.Create(new SessionRevocationViewModel("someone-else"));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, facade.RevokeCalls);
    }

    [Fact]
    public async Task TheRoutesAreNotInThePublicContract()
    {
        // They are internal, and the gateway 404s them from a browser. Publishing them in the OpenAPI document
        // would advertise a surface no client may call.
        using var api = Factory(new FakeIdentityFacade());
        using var client = api.CreateJsonClient();

        var document = await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("internal/sessions", document, StringComparison.Ordinal);
    }

    private static ApiFactory Factory(FakeIdentityFacade facade) =>
        new(configureServices: services => services.AddScoped<IIdentityFacade>(_ => facade));

    private static HttpRequestMessage Request(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return request;
    }
}
