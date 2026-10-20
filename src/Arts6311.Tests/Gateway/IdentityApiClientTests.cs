using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Arts6311.Gateway.Authentication;
using Arts6311.Gateway.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Arts6311.Tests.Gateway;

/// <summary>
/// The gateway's own calls to the internal API — the one path where it is a client rather than a proxy.
/// </summary>
/// <remarks>
/// Everywhere else this client is replaced by a fake, so without this nothing would assert the two things that
/// matter about it: that it presents a <em>service</em> token, and that it tells "the answer was no" apart from
/// "there was no answer".
/// </remarks>
public sealed class IdentityApiClientTests
{
    private static readonly string[] EditorRole = ["Editor"];

    [Fact]
    public async Task ACredentialCheckCarriesAServiceToken()
    {
        // A user token is refused on these routes however privileged the user (ADR 0021), so a client that signed
        // the wrong kind would fail every sign-in with a 403.
        using var handler = new RecordingHandler(Json(HttpStatusCode.OK, new
        {
            userId = "user-1",
            displayName = "Ada",
            roles = EditorRole,
            securityStamp = "stamp",
        }));

        var result = await Client(handler).CheckCredentialsAsync("ada@example.test", "secret", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal("user-1", result.User!.UserId);

        var token = new JsonWebToken(handler.Request!.Headers.Authorization!.Parameter);
        Assert.Equal("service", token.GetClaim("token_use").Value);
        Assert.Equal("api/v1/internal/sessions/credentials", handler.Request.RequestUri!.AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task ARefusalCarriesTheApisCodeBackToTheGateway()
    {
        // The gateway needs the distinction to word its log and the API needs it for the audit row; what must
        // not happen is it reaching a browser (ADR 0023), which BffSessionTests covers.
        using var handler = new RecordingHandler(
            Json(HttpStatusCode.Unauthorized, new { code = "auth.locked_out", title = "Locked." }));

        var result = await Client(handler).CheckCredentialsAsync("ada@example.test", "secret", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("auth.locked_out", result.FailureCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AFaultIsNotARefusal(HttpStatusCode status)
    {
        // Reported as "wrong password", a 500 would have people resetting passwords that are perfectly good,
        // and would hide an outage behind a plausible answer.
        using var handler = new RecordingHandler(new HttpResponseMessage(status));

        await Assert.ThrowsAsync<IdentityApiException>(
            () => Client(handler).CheckCredentialsAsync("ada@example.test", "secret", Ct));
    }

    [Fact]
    public async Task AValidationFailureIsAnExceptionRatherThanAnInvalidSession()
    {
        // "We could not ask" must not read as "this session is over": that would sign everybody out whenever the
        // API was briefly unreachable (ADR 0024).
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadGateway));

        await Assert.ThrowsAsync<IdentityApiException>(
            () => Client(handler).ValidateSessionAsync("user-1", "stamp", Ct));
    }

    [Fact]
    public async Task RevokingAnAlreadyMissingAccountSucceeds()
    {
        // Its sessions are over by another route, and the caller still has a ticket to delete.
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.True(await Client(handler).RevokeSessionsAsync("user-1", Ct));
    }

    [Fact]
    public async Task ThePasswordIsSentOnceAndAppearsNowhereElse()
    {
        using var handler = new RecordingHandler(Json(HttpStatusCode.OK, new
        {
            userId = "user-1",
            displayName = "Ada",
            roles = Array.Empty<string>(),
            securityStamp = "stamp",
        }));

        await Client(handler).CheckCredentialsAsync("ada@example.test", "hunter2-and-then-some", Ct);

        Assert.DoesNotContain(
            "hunter2", handler.Request!.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            "hunter2",
            string.Join(';', handler.Request.Headers.Select(header => $"{header.Key}={string.Join(',', header.Value)}")),
            StringComparison.Ordinal);
        Assert.Contains("hunter2", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedPasswordBringsBackItsCodeAndReasons()
    {
        // The shape the API writes for auth.password_rejected (ApiProblemDetails over the field errors Business
        // keys "newPassword"; Identity/PasswordResetTests pins that side). A renamed key would silently drop every
        // reason the person needs to choose a better password.
        using var handler = new RecordingHandler(Json(HttpStatusCode.BadRequest, new
        {
            code = "auth.password_rejected",
            title = "The request was not valid.",
            errors = new Dictionary<string, string[]>
            {
                ["newPassword"] = ["A staff password must be at least 14 characters."],
            },
        }));

        var result = await Client(handler).CompletePasswordResetAsync("user-1", "token", "too-short", Ct);

        Assert.Equal("auth.password_rejected", result.FailureCode);
        Assert.Equal(["A staff password must be at least 14 characters."], result.PasswordErrors);
        Assert.Equal(
            "api/v1/internal/identity/password-reset/complete", handler.Request!.RequestUri!.AbsolutePath.TrimStart('/'));
    }

    [Fact]
    public async Task A204IsAUsedLink()
    {
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NoContent));

        Assert.True((await Client(handler).ConfirmEmailAsync("user-1", "token", Ct)).Succeeded);
    }

    [Fact]
    public async Task A400WithoutACodeIsStillTheOneRefusal()
    {
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));

        var result = await Client(handler).ConfirmEmailAsync("user-1", "token", Ct);

        Assert.Equal("auth.token_invalid", result.FailureCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task AFaultUsingALinkIsNotARefusal(HttpStatusCode status)
    {
        // Reported as "this link has expired", an outage would send people round the reset loop for nothing.
        using var handler = new RecordingHandler(new HttpResponseMessage(status));

        await Assert.ThrowsAsync<IdentityApiException>(
            () => Client(handler).CompletePasswordResetAsync("user-1", "token", "a-new-passphrase", Ct));
    }

    [Fact]
    public async Task AResetRequestExpects202AndNothingElse()
    {
        using var accepted = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Accepted));
        await Client(accepted).RequestPasswordResetAsync("ada@example.test", Ct);
        Assert.Contains("ada@example.test", accepted.Body!, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", accepted.Request!.RequestUri!.ToString(), StringComparison.Ordinal);

        using var refused = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<IdentityApiException>(
            () => Client(refused).RequestPasswordResetAsync("ada@example.test", Ct));
    }

    [Fact]
    public async Task AConfirmationRequestPutsTheAddressInTheBodyAndTreatsAnythingButA202AsNoAnswer()
    {
        using var accepted = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Accepted));
        await Client(accepted).RequestEmailConfirmationAsync("ada@example.test", Ct);
        Assert.Equal(
            "api/v1/internal/identity/email-confirmation", accepted.Request!.RequestUri!.AbsolutePath.TrimStart('/'));
        Assert.Contains("ada@example.test", accepted.Body!, StringComparison.Ordinal);

        using var refused = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<IdentityApiException>(
            () => Client(refused).RequestEmailConfirmationAsync("ada@example.test", Ct));
    }

    private static IdentityApiClient Client(RecordingHandler handler)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = new InternalTokenOptions { SigningKeyPem = key.ExportPkcs8PrivateKeyPem() };

        return new IdentityApiClient(
            new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") },
            new InternalTokenSigner(options, TimeProvider.System),
            NullLogger<IdentityApiClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body) };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Answers with one scripted response and keeps the request it was given.</summary>
    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : Encoding.UTF8.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken));

            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
