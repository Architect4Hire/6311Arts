using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Gateway.Edge;

/// <summary>
/// The gateway's own <c>ProblemDetails</c>, with a stable <c>code</c> and the correlation id
/// (<c>gateway.md</c>: "A correlation id is created or propagated and returned in ProblemDetails").
/// </summary>
/// <remarks>
/// A deliberate duplicate of the three fields <c>Arts6311.Api/Conventions/ApiProblemDetails.cs</c> writes. The
/// gateway cannot reference the API — it holds no domain code, and <c>ProjectReferenceTests</c> keeps it that way —
/// so a client sees the same envelope from both without the two sharing a type (ADR 0022). The codes below are
/// the gateway's; the API's own codes come from <c>ErrorCodes</c>.
/// </remarks>
internal static class GatewayProblem
{
    /// <summary>Codes for the statuses the edge itself produces. They match the API's spelling of the same ideas.</summary>
    private static readonly Dictionary<int, (string Code, string Title)> ByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = ("request.rejected", "The request was rejected."),
        [StatusCodes.Status401Unauthorized] = ("auth.unauthenticated", "Sign in to continue."),
        [StatusCodes.Status403Forbidden] = ("access.forbidden", "This account may not do that."),
        [StatusCodes.Status404NotFound] = ("request.route_not_found", "The resource was not found."),
        [StatusCodes.Status413PayloadTooLarge] = ("request.payload_too_large", "The request was too large."),
        [StatusCodes.Status429TooManyRequests] = ("request.rate_limited", "Too many requests."),
        [StatusCodes.Status502BadGateway] = ("gateway.upstream_failed", "The request could not be completed."),
        [StatusCodes.Status503ServiceUnavailable] = ("gateway.upstream_unavailable", "The service is unavailable."),
        [StatusCodes.Status504GatewayTimeout] = ("gateway.upstream_timeout", "The request could not be completed."),
    };

    /// <summary>Codes the edge raises for a specific reason, rather than for a status on its own.</summary>
    internal static class Codes
    {
        /// <summary>An unsafe request arrived without a usable antiforgery token (<c>auth.md</c> "CSRF").</summary>
        public const string AntiforgeryRequired = "auth.antiforgery_required";

        /// <summary>
        /// Sign-in failed. The single answer for a wrong password, an unknown address, a lockout and an
        /// unconfirmed staff address alike: the API distinguishes them for us, and a browser told which one can
        /// enumerate accounts (ADR 0023).
        /// </summary>
        public const string CredentialsInvalid = "auth.credentials_invalid";

        /// <summary>The body was not the shape the route reads.</summary>
        public const string ValidationFailed = "validation.failed";

        /// <summary>The internal API could not be reached, so the question could not be answered.</summary>
        public const string IdentityUnavailable = "auth.identity_unavailable";
    }

    public static string CodeFor(int status) =>
        ByStatus.TryGetValue(status, out var known) ? known.Code : "gateway.request_failed";

    public static Task WriteAsync(HttpContext context, int status)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (code, title) = ByStatus.TryGetValue(status, out var known)
            ? known
            : (CodeFor(status), "The request failed.");

        return WriteAsync(context, status, code, title);
    }

    /// <summary>
    /// The same envelope with a code the caller chose, for the refusals where the status alone does not say
    /// enough — a 400 that means "fetch a new antiforgery token" is acted on differently from any other 400.
    /// </summary>
    public static async Task WriteAsync(HttpContext context, int status, string code, string title)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Response.HasStarted)
        {
            // The proxy has already begun streaming. Nothing can be written over it, and trying would corrupt the
            // response the client is reading.
            return;
        }

        // Deliberately not Response.Clear(): that resets the whole header collection, taking the security headers
        // SecurityHeadersMiddleware has already written and any Retry-After the rate limiter just set. Only the
        // status and the body belong to this method.
        context.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Extensions =
            {
                ["code"] = code,

                // The W3C trace id (ADR 0019), so a support request and the dashboard's trace search use the same
                // value. ASP.NET Core has already parsed any inbound traceparent into this activity.
                ["correlationId"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
            },
        };

        // The contentType argument matters: the two-argument overload writes application/json and would override
        // anything set on the response beforehand.
        await context.Response.WriteAsJsonAsync(
            problem, options: null, contentType: "application/problem+json", context.RequestAborted);
    }
}
