using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arts6311.Gateway.Authentication;

namespace Arts6311.Gateway.Session;

/// <summary>
/// Calls <c>/api/v1/internal/sessions/**</c> with a freshly signed <c>token_use=service</c> token.
/// </summary>
/// <remarks>
/// <para>
/// This is the one path on which the gateway is a client rather than a proxy. The token is signed per call and
/// lives two minutes (ADR 0002); nothing is cached, because a cached credential is a credential with a longer
/// life than the one we chose.
/// </para>
/// <para>
/// The base address is <c>https+http://api</c>, resolved by Aspire service discovery — no host or port appears
/// here or in configuration (<c>aspire.md</c>). The gateway's own <c>/api/*/internal/**</c> block is an
/// <em>inbound</em> middleware, so it does not stand in the way of this call.
/// </para>
/// </remarks>
internal sealed partial class IdentityApiClient(
    HttpClient client, IInternalTokenSigner signer, ILogger<IdentityApiClient> logger) : IIdentityApiClient
{
    private const string Credentials = "api/v1/internal/sessions/credentials";
    private const string Validate = "api/v1/internal/sessions/validate";
    private const string Revoke = "api/v1/internal/sessions/revoke";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CredentialCheckResult> CheckCredentialsAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            Credentials, new { email, password }, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<UserSummary>(Json, cancellationToken) is { } summary
                ? new CredentialCheckResult(summary, null)
                : throw new IdentityApiException("The credential route answered with no body.");
        }

        // Only a refusal is a refusal. A 500 or a 503 reported as "wrong password" would have people resetting
        // passwords that are perfectly good, and would hide an outage behind a plausible answer.
        if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden))
        {
            throw new IdentityApiException($"The credential route answered {(int)response.StatusCode}.");
        }

        return new CredentialCheckResult(null, await ReadCodeAsync(response, cancellationToken));
    }

    public async Task<SessionValidationResult> ValidateSessionAsync(
        string userId, string securityStamp, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            Validate, new { userId, securityStamp }, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A refusal is not an answer. Throwing lets the caller distinguish "this session is over" from "the
            // API could not tell us", which must not end a session (ADR 0024).
            throw new IdentityApiException(
                $"The session validation route answered {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<SessionValidationResult>(Json, cancellationToken)
            ?? throw new IdentityApiException("The session validation route answered with no body.");
    }

    public async Task<bool> RevokeSessionsAsync(string userId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(Revoke, new { userId }, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The account is gone, so its sessions are over by a different route. The caller still deletes the
            // ticket it holds, which is the whole point of the request.
            LogRevocationMissedAccount(logger);
            return true;
        }

        throw new IdentityApiException($"The session revocation route answered {(int)response.StatusCode}.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Session revocation found no such account.")]
    private static partial void LogRevocationMissedAccount(ILogger logger);

    private async Task<HttpResponseMessage> SendAsync(
        string route, object payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(payload, options: Json),
        };

        // token_use=service. A user token is refused on these routes however privileged the user (ADR 0021).
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", signer.SignForService());

        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up — the browser disconnected. Not our failure to report.
            throw;
        }
        catch (Exception exception)
        {
            // Everything else becomes one type, because every caller has to answer the same question: was this
            // "no" or "no answer"? The resilience pipeline ServiceDefaults adds throws its own
            // (TimeoutRejectedException, BrokenCircuitException), and a caller catching only HttpRequestException
            // would turn an API outage into a 500 instead of the 503 it means.
            throw new IdentityApiException($"The internal API call to '{route}' did not complete.", exception);
        }
    }

    /// <summary>
    /// The <c>code</c> from the API's <c>ProblemDetails</c>, or null when the body is not one. Only the code is
    /// read: the message is written for the gateway's log, not for a browser.
    /// </summary>
    private static async Task<string?> ReadCodeAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The internal API could not be reached or did not answer usefully. Deliberately not the same as "the answer
/// was no".
/// </summary>
internal sealed class IdentityApiException : Exception
{
    public IdentityApiException(string message) : base(message)
    {
    }

    public IdentityApiException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public IdentityApiException()
    {
    }
}
