using Microsoft.AspNetCore.Antiforgery;

namespace Arts6311.Gateway.Edge;

/// <summary>
/// The antiforgery pair (<c>auth.md</c> "CSRF"): a secret cookie the browser cannot read, and a request token it
/// can, which it echoes in the <c>X-XSRF-TOKEN</c> header on every unsafe request.
/// </summary>
/// <remarks>
/// <para>
/// Two cookies, because that is what the scheme needs: the HttpOnly half is the secret, and script has to be
/// able to read the other half to put it in a header. Script cannot read the secret, so a cross-site page cannot
/// construct a matching pair.
/// </para>
/// <para>
/// <strong>Both are delivered as cookies and never in a response body.</strong> The gateway compresses
/// responses, and a secret in a compressed body alongside attacker-controlled content is a BREACH oracle — the
/// obligation ADR 0022 left for this prompt.
/// </para>
/// </remarks>
internal static class AntiforgeryCookie
{
    /// <summary>The secret half. <c>HttpOnly</c>; configured on the antiforgery service itself.</summary>
    public const string CookieTokenName = "__Host-6311.xsrf";

    /// <summary>The half script reads to populate the header. Not a secret on its own.</summary>
    public const string RequestTokenName = "__Host-6311.csrf";

    /// <summary>
    /// Where the request token is expected. The Angular apps set this as their XSRF header name, reading
    /// <see cref="RequestTokenName"/> (a 3.3 obligation, recorded in ADR 0024).
    /// </summary>
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>
    /// Issues both halves for whoever <c>HttpContext.User</c> currently is. Callers that change the identity must
    /// set it first: the token is bound to it.
    /// </summary>
    public static void Issue(HttpContext context, IAntiforgery antiforgery)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(antiforgery);

        // Writes the HttpOnly cookie-token cookie as a side effect, and hands back the request token.
        var tokens = antiforgery.GetAndStoreTokens(context);

        if (tokens.RequestToken is not { Length: > 0 } requestToken)
        {
            return;
        }

        context.Response.Cookies.Append(RequestTokenName, requestToken, new CookieOptions
        {
            // Deliberately readable: script has to put this value in the header. The secret stays in the
            // HttpOnly cookie above, so reading this one buys a cross-site page nothing.
            HttpOnly = false,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
        });
    }
}
