namespace Arts6311.Gateway.Session;

/// <summary>
/// The one cookie the browser holds (<c>auth.md</c>): <c>__Host-6311.session</c>, <c>HttpOnly</c>,
/// <c>Secure</c>, <c>SameSite=Lax</c>, <c>Path=/</c>, carrying a ticket reference and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <c>SameSite=Lax</c> rather than <c>Strict</c>: a staff member following a link into <c>/admin</c> from an
/// email must arrive signed in, and <c>Strict</c> would drop the cookie on that first navigation. Lax still
/// withholds it from cross-site <c>POST</c>s, and every unsafe request needs antiforgery besides.
/// </para>
/// <para>
/// <strong>No <c>Expires</c> or <c>Max-Age</c>:</strong> it is a browser-session cookie, so closing the browser
/// ends it, and the server-side ticket is the authority on every other limit. A persistent cookie would have to
/// be rewritten on every response to track the sliding window, and would leave a usable reference on disk for
/// whatever the ticket's remaining life is.
/// </para>
/// </remarks>
internal static class SessionCookie
{
    public static string? Read(HttpContext context, BrowserSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        return context.Request.Cookies[options.CookieName] is { Length: > 0 } value ? value : null;
    }

    public static void Issue(HttpContext context, BrowserSessionOptions options, string ticketKey)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        context.Response.Cookies.Append(options.CookieName, ticketKey, Options());
    }

    /// <summary>
    /// Clears the cookie. Called on sign-out, and also whenever a cookie arrives for a ticket that no longer
    /// exists — a dead reference the browser would otherwise keep presenting on every request.
    /// </summary>
    public static void Clear(HttpContext context, BrowserSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        context.Response.Cookies.Delete(options.CookieName, Options());
    }

    /// <summary>
    /// The attributes, in one place. <c>Delete</c> takes them too: a browser matches the deletion against path
    /// and security attributes, so a mismatch leaves the cookie in place.
    /// </summary>
    private static CookieOptions Options() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",

        // Never suppressed by a cookie-consent policy: without it there is no session at all.
        IsEssential = true,
    };
}
