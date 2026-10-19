namespace Arts6311.Gateway.Edge;

/// <summary>
/// Answers 404 for <c>/api/{version}/internal/**</c>, always (<c>gateway.md</c>, <c>auth.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// 404 rather than 403: a browser must not be able to tell an internal route from one that does not exist. The API
/// also refuses these paths without a <c>token_use=service</c> token, so this is the outer of two locks — but it
/// is the one that keeps the routes from being discoverable at all.
/// </para>
/// <para>
/// Middleware, not a YARP route: a YARP route needs a cluster, and there is nothing here to forward to. Matching on
/// path segments rather than a literal prefix means <c>/api/v2/internal/...</c> is covered the day v2 exists,
/// without anyone remembering to add it.
/// </para>
/// </remarks>
internal sealed class InternalPathBlockMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return IsInternalPath(context.Request.Path)
            ? GatewayProblem.WriteAsync(context, StatusCodes.Status404NotFound)
            : next(context);
    }

    /// <summary>
    /// <c>/api/&lt;anything&gt;/internal</c> and anything below it. The version segment is not inspected: a route
    /// family is internal whatever version it belongs to.
    /// </summary>
    /// <remarks>
    /// The path is percent-decoded before it is split, and each segment is cut at the first <c>;</c>. Without
    /// that, <c>/api/v1/internal%2Fsessions</c> and <c>/api/v1/internal;x/sessions</c> read as a single segment
    /// that is not exactly "internal", and both slip past to be forwarded. They happen to 404 at the API today
    /// because no route matches them — but that is the API's routing being unhelpful to an attacker by accident,
    /// not this block doing its job, and 1.9 is about to add real routes under that prefix.
    /// </remarks>
    internal static bool IsInternalPath(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        // Decoding first means an encoded slash becomes a separator, which is the safe direction: it can only
        // reveal an "internal" segment somebody tried to hide, never invent one at this position.
        var decoded = Uri.UnescapeDataString(path.Value!);

        var segments = decoded
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Split(';', 2)[0])
            .ToArray();

        return segments.Length >= 3
            && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("internal", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class InternalPathBlockExtensions
{
    public static IApplicationBuilder UseInternalPathBlock(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<InternalPathBlockMiddleware>();
    }
}
