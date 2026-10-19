namespace Arts6311.Gateway.Edge;

/// <summary>
/// Drops the forwarding headers a client sent, so nothing downstream can mistake them for the edge's own
/// (<c>gateway.md</c> "Headers": forwarded headers come only from configured trusted proxies).
/// </summary>
/// <remarks>
/// <para>
/// Runs <em>after</em> <c>UseForwardedHeaders</c>, which is what reads and validates these headers when a trusted
/// proxy did send them — in Azure, Container Apps' ingress. That middleware consumes the entries it accepts, so
/// whatever is still here afterwards came from somebody untrusted and is removed.
/// </para>
/// <para>
/// Stripping inbound, rather than with a YARP <c>RequestHeaderRemove</c> transform, because YARP's own
/// <c>X-Forwarded</c> transform owns those header names on the proxy request and overrides a remove transform for
/// the same name. Removing them from the inbound request leaves the transform free to write the true values, and
/// makes the result independent of whether a peer address happens to be available — which it is not on a
/// connectionless test server, and that is exactly the case where a silent fallback would hide a spoofed value.
/// </para>
/// </remarks>
internal sealed class TrustedHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The four YARP manages plus RFC 7239's <c>Forwarded</c>, which YARP does not touch unless its own
    /// <c>Forwarded</c> transform is enabled.
    /// </summary>
    private static readonly string[] ClientForwardingHeaders =
    [
        "X-Forwarded-For",
        "X-Forwarded-Proto",
        "X-Forwarded-Host",
        "X-Forwarded-Prefix",
        "X-Forwarded-Port",
        "X-Forwarded-Server",
        "X-Forwarded-Client-Cert",
        "Forwarded",

        // What ForwardedHeadersMiddleware itself writes when it consumes a header. A client can pre-set them when
        // no proxy is trusted, which would make its own values look like ones the edge had processed.
        "X-Original-For",
        "X-Original-Proto",
        "X-Original-Host",

        // Vendor spellings of the same idea. Nothing downstream reads them today; they are removed so that a
        // later component which does cannot be fed by a client.
        "X-Real-IP",
        "X-Client-IP",
        "True-Client-IP",
        "CF-Connecting-IP",
    ];

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var header in ClientForwardingHeaders)
        {
            context.Request.Headers.Remove(header);
        }

        return next(context);
    }
}

internal static class TrustedHeadersExtensions
{
    public static IApplicationBuilder UseTrustedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<TrustedHeadersMiddleware>();
    }
}
