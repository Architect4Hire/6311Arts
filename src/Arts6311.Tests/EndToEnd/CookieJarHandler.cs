namespace Arts6311.Tests.EndToEnd;

/// <summary>
/// A test-only cookie jar: it keeps every cookie by name, sends them all back, and remembers the last raw
/// <c>Set-Cookie</c> header for each so a test can assert the attributes the gateway really sent over TLS.
/// </summary>
/// <remarks>
/// Not <see cref="System.Net.CookieContainer"/>, which parses attributes away and silently drops what it judges
/// invalid — exactly what these tests need to see. The jar enforces nothing itself; the assertions do. It has no place
/// outside the test assembly.
/// </remarks>
internal sealed class CookieJarHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly Dictionary<string, string> cookies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> rawSetCookies = new(StringComparer.Ordinal);

    /// <summary>The last <c>Set-Cookie</c> header received for this cookie name, attributes and all.</summary>
    public string? LastSetCookie(string name)
    {
        lock (cookies)
        {
            return rawSetCookies.GetValueOrDefault(name);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (cookies)
        {
            if (cookies.Count > 0)
            {
                request.Headers.Remove("Cookie");
                request.Headers.TryAddWithoutValidation(
                    "Cookie", string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            lock (cookies)
            {
                foreach (var setCookie in setCookies)
                {
                    Store(setCookie);
                }
            }
        }

        return response;
    }

    private void Store(string setCookie)
    {
        var pair = setCookie.Split(';', 2)[0];
        var separator = pair.IndexOf('=', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return;
        }

        var name = pair[..separator].Trim();
        var value = pair[(separator + 1)..].Trim();
        rawSetCookies[name] = setCookie;

        // A clearing Set-Cookie carries an empty value (and a past expiry): the browser drops the cookie.
        var expired = setCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)
            || setCookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase);

        if (value.Length == 0 || expired)
        {
            cookies.Remove(name);
        }
        else
        {
            cookies[name] = value;
        }
    }
}
