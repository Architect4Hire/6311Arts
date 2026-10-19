using Microsoft.AspNetCore.Antiforgery;

namespace Arts6311.Gateway.Edge;

/// <summary>
/// Every unsafe request needs an antiforgery token (<c>auth.md</c> "CSRF"), checked here — at the edge, for the
/// whole origin, including the requests that are about to be proxied.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The exemption list is exactly one path.</strong> <c>/webhooks/square</c> is Square calling us, with no
/// browser and no session, and it is verified by signature instead (<c>outbox.md</c>). Everything else — the BFF's
/// own endpoints, the CMS API, forms, checkout — is covered without the route having to ask.
/// </para>
/// <para>
/// It runs <strong>after</strong> authentication and <strong>before</strong> authorization. After, because an
/// antiforgery token is bound to the signed-in identity: validated against an anonymous principal, every token a
/// signed-in person holds is refused, which looks exactly like a CSRF bug and was found by a test. Before
/// authorization, so a missing token on a protected route is a 400 — the answer <c>auth.md</c>'s test list
/// calls for, and one a client can act on by fetching a new token. Either way it runs before the proxy, so an
/// unverified write never reaches the API.
/// </para>
/// <para>
/// Validation reads the <c>X-XSRF-TOKEN</c> header, never a form field, so the request body is not buffered here
/// and uploads still stream through.
/// </para>
/// </remarks>
internal sealed class AntiforgeryEdgeMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    /// <summary>Signature-verified instead. The only exemption, listed explicitly (<c>auth.md</c>).</summary>
    private const string WebhookPath = "/webhooks/square";

    /// <summary>Where the request token must be. Mirrors the configured header name.</summary>
    private const string HeaderName = AntiforgeryCookie.HeaderName;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (NeedsToken(context.Request))
        {
            if (context.Request.HasFormContentType && !context.Request.Headers.ContainsKey(HeaderName))
            {
                // Refused without asking the antiforgery service, which would read the form looking for a field
                // token — buffering an attacker-controlled body at the edge, and opening a second way to present
                // a token that nothing else in this system uses. The header is the only way in.
                await GatewayProblem.WriteAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    GatewayProblem.Codes.AntiforgeryRequired,
                    "This request needs a current antiforgery token.");

                return;
            }

            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                // A distinct code, because the client's correct response is specific: fetch /bff/antiforgery and
                // try again. The reason is never echoed — which half was missing or stale is of no use to a
                // caller and of some use to an attacker.
                await GatewayProblem.WriteAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    GatewayProblem.Codes.AntiforgeryRequired,
                    "This request needs a current antiforgery token.");

                return;
            }
        }

        await next(context);
    }

    private static bool NeedsToken(HttpRequest request) =>
        !IsSafeMethod(request.Method) && !IsTheWebhook(request);

    /// <summary>
    /// The webhook itself, and nothing below it. A prefix match would also exempt
    /// <c>/webhooks/square/anything</c> — which is not a route, so it falls through to the site catch-all and
    /// would reach the SSR app as an unchecked write.
    /// </summary>
    private static bool IsTheWebhook(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && (request.Path.Equals(WebhookPath, StringComparison.OrdinalIgnoreCase)
            || request.Path.Equals(WebhookPath + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The methods defined as not changing state. <c>OPTIONS</c> and <c>TRACE</c> are included because a browser
    /// preflight carries no token and would otherwise be refused before the request it belongs to.
    /// </summary>
    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method)
        || HttpMethods.IsHead(method)
        || HttpMethods.IsOptions(method)
        || HttpMethods.IsTrace(method);
}

internal static class AntiforgeryEdgeExtensions
{
    public static IApplicationBuilder UseArtsAntiforgery(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<AntiforgeryEdgeMiddleware>();
    }
}
