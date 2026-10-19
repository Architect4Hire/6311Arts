using System.Net.Http.Json;
using Arts6311.Gateway.Edge;

namespace Arts6311.Tests.Gateway.Infrastructure;

/// <summary>
/// A browser, roughly: it keeps cookies, fetches an antiforgery token before its first unsafe request, and
/// echoes the readable half in the <c>X-XSRF-TOKEN</c> header — exactly what the Angular apps will do.
/// </summary>
internal sealed class BffClient(HttpClient client) : IDisposable
{
    private string? requestToken;

    /// <summary>
    /// The test's own token, taken from the ambient context rather than a parameter: every call here is a test
    /// step, and xUnit's analyzer wants this token on each of them.
    /// </summary>
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public HttpClient Http => client;

    /// <summary>The session cookie this client currently holds, if any. Lets a test copy or forge one.</summary>
    public string? SessionCookie { get; private set; }

    /// <summary>The antiforgery request token it would send next, for the test that keeps a stale one.</summary>
    public string? RequestToken => requestToken;

    public async Task<HttpResponseMessage> PostAsync(
        string path, object? body = null, bool withAntiforgery = true)
    {
        if (withAntiforgery)
        {
            await EnsureAntiforgeryAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (withAntiforgery && requestToken is { } token)
        {
            request.Headers.TryAddWithoutValidation(AntiforgeryCookie.HeaderName, token);
        }

        var response = await client.SendAsync(request, Cancellation);
        Capture(response);

        return response;
    }

    public async Task<HttpResponseMessage> GetAsync(string path, string? accept = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (accept is not null)
        {
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd(accept);
        }

        var response = await client.SendAsync(request, Cancellation);
        Capture(response);

        return response;
    }

    public async Task EnsureAntiforgeryAsync()
    {
        if (requestToken is not null)
        {
            return;
        }

        using var response = await client.GetAsync("/bff/antiforgery", Cancellation);
        Capture(response);
    }

    /// <summary>
    /// Forgets the antiforgery token without forgetting the cookies, so the next unsafe request fetches a fresh
    /// pair — what a client does after signing in, since the token is bound to the identity.
    /// </summary>
    public void ForgetAntiforgeryToken() => requestToken = null;

    private void Capture(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        foreach (var cookie in cookies)
        {
            if (Value(cookie, AntiforgeryCookie.RequestTokenName) is { Length: > 0 } token)
            {
                requestToken = token;
            }

            if (Value(cookie, "__Host-6311.session") is { } session)
            {
                SessionCookie = session.Length > 0 ? session : null;
            }
        }
    }

    private static string? Value(string setCookie, string name)
    {
        var prefix = name + "=";

        if (!setCookie.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var value = setCookie[prefix.Length..];
        var end = value.IndexOf(';', StringComparison.Ordinal);

        return end < 0 ? value : value[..end];
    }

    public void Dispose() => client.Dispose();
}
