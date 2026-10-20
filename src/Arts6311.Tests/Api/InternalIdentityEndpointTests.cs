using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Tests.Api.Infrastructure;
using Arts6311.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arts6311.Tests.Api;

/// <summary>
/// The internal reset and confirmation routes (auth.md, ADR 0028): a service token and nothing else, and the
/// answers the gateway reads.
/// </summary>
/// <remarks>The facade is faked; the rules run against real Identity in <c>Identity/PasswordResetTests</c>.</remarks>
public sealed class InternalIdentityEndpointTests
{
    private const string Request = "/api/v1/internal/identity/password-reset";
    private const string Complete = "/api/v1/internal/identity/password-reset/complete";
    private const string Confirm = "/api/v1/internal/identity/email-confirmation/complete";

    private const string LinkToken = "CfDJ8-a-reset-token-that-must-never-be-logged";
    private const string NewPhrase = "a-new-passphrase-that-must-never-be-logged";
    private const string ShortPhrase = "short";

    [Fact]
    public async Task AServiceTokenRequestsAResetAndIsAnswered202WithNoBody()
    {
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        var response = await SendAsync(client, Request, api.Tokens.ServiceToken(), new { email = "m@6311arts.test" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("m@6311arts.test", facade.ResetRequest!.Email);
    }

    [Fact]
    public async Task AServiceTokenCompletesAReset()
    {
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        var response = await SendAsync(
            client, Complete, api.Tokens.ServiceToken(), new { userId = "user-1", token = LinkToken, newPassword = NewPhrase });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(("user-1", LinkToken, NewPhrase), (
            facade.ResetCompletion!.UserId, facade.ResetCompletion.Token, facade.ResetCompletion.NewPassword));
    }

    [Fact]
    public async Task AServiceTokenConfirmsAnAddress()
    {
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        var response = await SendAsync(
            client, Confirm, api.Tokens.ServiceToken(), new { userId = "user-1", token = LinkToken });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("user-1", facade.Confirmation!.UserId);
    }

    [Theory]
    [InlineData(Request, "Admin")]
    [InlineData(Complete, "Admin")]
    [InlineData(Confirm, "Admin")]
    [InlineData(Complete, "Member")]
    public async Task AUserTokenIs403EvenForAnAdmin(string route, string role)
    {
        // A user token here would let a signed-in person reset someone else's password by naming their id.
        var facade = new FakeIdentityFacade();
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        var response = await SendAsync(
            client, route, api.Tokens.UserToken(role),
            new { email = "m@6311arts.test", userId = "user-1", token = LinkToken, newPassword = NewPhrase });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(facade.ResetRequest);
        Assert.Null(facade.ResetCompletion);
        Assert.Null(facade.Confirmation);
    }

    [Fact]
    public async Task NoTokenIs401()
    {
        using var api = Factory(new FakeIdentityFacade());
        using var client = api.CreateJsonClient();

        var response = await client.PostAsJsonAsync(
            Complete, new { userId = "u", token = LinkToken, newPassword = NewPhrase }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnUnusableLinkIsA400CarryingItsCode()
    {
        var facade = new FakeIdentityFacade
        {
            Error = OperationError.Of(IdentityErrorCodes.TokenInvalid, "This link has expired or has already been used."),
        };
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        var response = await SendAsync(
            client, Confirm, api.Tokens.ServiceToken(), new { userId = "user-1", token = LinkToken });

        // The gateway branches on the code, not the status (ADR 0023).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(IdentityErrorCodes.TokenInvalid, body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ARejectedPasswordCarriesItsReasonsUnderNewPassword()
    {
        // The field name is the contract the gateway reads (IdentityApiClient.ReadRefusalAsync).
        var facade = new FakeIdentityFacade
        {
            Error = new OperationError(
                IdentityErrorCodes.PasswordRejected,
                "That password cannot be used.",
                new Dictionary<string, string[]> { ["newPassword"] = ["A staff password must be at least 14 characters."] }),
        };
        using var api = Factory(facade);
        using var client = api.CreateJsonClient();

        var response = await SendAsync(
            client, Complete, api.Tokens.ServiceToken(), new { userId = "user-1", token = LinkToken, newPassword = ShortPhrase });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(IdentityErrorCodes.PasswordRejected, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "A staff password must be at least 14 characters.",
            body.RootElement.GetProperty("errors").GetProperty("newPassword")[0].GetString());
    }

    [Fact]
    public async Task TheRoutesAreNotInThePublicContract()
    {
        using var api = Factory(new FakeIdentityFacade());
        using var client = api.CreateJsonClient();

        var document = await client.GetStringAsync("/openapi/v1.json", Ct);

        Assert.DoesNotContain("internal/identity", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTokenAndTheNewPasswordAreNeverLogged()
    {
        // Everything, from every category, at Trace: the guarantee must not rest on production log levels.
        var sink = new CapturingLoggerProvider();
        using var api = new ApiFactory(configureServices: services =>
        {
            services.AddScoped<IIdentityFacade>(_ => new FakeIdentityFacade());
            services.AddLogging(logging =>
            {
                logging.AddProvider(sink);
                logging.SetMinimumLevel(LogLevel.Trace);
            });
        });
        using var client = api.CreateJsonClient();

        await SendAsync(
            client, Complete, api.Tokens.ServiceToken(), new { userId = "user-1", token = LinkToken, newPassword = NewPhrase });

        // A malformed body as well: a model-binding failure is where a framework is most tempted to quote input.
        using var malformed = new HttpRequestMessage(HttpMethod.Post, Complete)
        {
            Content = new StringContent(
                "{\"token\":\"" + LinkToken + "\",\"newPassword\":\"" + NewPhrase + "\",",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        malformed.Headers.TryAddWithoutValidation("Authorization", "Bearer " + api.Tokens.ServiceToken());
        await client.SendAsync(malformed, Ct);

        var logged = sink.Text();
        Assert.NotEmpty(logged);
        Assert.DoesNotContain(LinkToken, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPhrase, logged, StringComparison.Ordinal);
    }

    private static ApiFactory Factory(FakeIdentityFacade facade) =>
        new(configureServices: services => services.AddScoped<IIdentityFacade>(_ => facade));

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);

        return await client.SendAsync(request, Ct);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
