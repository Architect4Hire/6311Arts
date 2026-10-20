using System.Diagnostics.CodeAnalysis;
using Arts6311.Gateway.Authentication;
using Arts6311.Gateway.Edge;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Gateway.Session;

/// <summary>
/// The browser-facing session endpoints (<c>auth.md</c> "Browser session"). They are the gateway's own, not
/// proxied: the API has no browser-facing auth surface at all.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nothing here ever gives the browser a token.</strong> The response to a sign-in is a cookie carrying
/// an opaque ticket reference, and a body with a display name and roles. The internal token is minted per
/// proxied request and never leaves the server (ADR 0002).
/// </para>
/// <para>
/// These routes outrank the <c>site</c> catch-all by ASP.NET's most-specific-wins precedence, so no YARP route
/// entry is needed for <c>/bff/**</c> and none exists.
/// </para>
/// </remarks>
internal static partial class BffEndpoints
{
    public const string BasePath = "/bff";

    public static IEndpointRouteBuilder MapBff(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var bff = endpoints.MapGroup(BasePath);

        // Every answer here is personal — a display name, roles, a Set-Cookie — and gateway.md forbids caching a
        // personalized response. Applied to the group rather than per endpoint, so the next one added cannot
        // forget. Vary: Cookie because the answer depends on the session cookie and nothing else.
        bff.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.Headers.Vary = "Cookie";

            return await next(context);
        });

        // Anonymous and safe: a browser needs the pair before it can sign in, since login is itself an unsafe
        // request and carries antiforgery like any other.
        bff.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            AntiforgeryCookie.Issue(context, antiforgery);
            return Results.NoContent();
        });

        bff.MapGet("/session", Session);

        bff.MapPost("/login", LoginAsync)

            // The rate limit auth.md says is never relaxed without an ADR. Per client IP, because there is no
            // session to partition by yet (gateway.md).
            .RequireRateLimiting(RateLimitExtensions.Policies.BffLogin);

        // No authorization: signing out without a session is not an error, and answering 401 to "end my session"
        // leaves a browser with a cookie it cannot get rid of.
        bff.MapPost("/logout", LogoutAsync);

        bff.MapPost("/logout-everywhere", LogoutEverywhereAsync)
            .RequireAuthorization(GatewayPolicies.Session);

        // Forgot, reset and confirm (BffEndpoints.Account.cs). In this group, so the no-store filter covers them.
        MapAccount(bff);

        return endpoints;
    }

    /// <summary>Display name and roles. Never an id, an email address, a stamp or a token (<c>auth.md</c>).</summary>
    private static IResult Session(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? Results.Ok(new SessionResponse(
                context.User.FindFirst(SessionPrincipal.NameClaim)?.Value ?? string.Empty,
                [.. context.User.FindAll(InternalTokenDefaults.RoleClaim).Select(claim => claim.Value)]))

            // Deliberately not RequireAuthorization: this route answers 401 even to a browser navigation, where
            // the challenge would redirect. A client asking "am I signed in?" wants the answer, not a login page.
            : Problem(StatusCodes.Status401Unauthorized, GatewayProblem.CodeFor(StatusCodes.Status401Unauthorized));

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        HttpContext context,
        IIdentityApiClient identity,
        ISessionManager sessions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(BffEndpoints).FullName!);

        if (!IsPlausible(request))
        {
            // Shape only, and the same answer for either field: the API validates it again, and a message naming
            // which field was wrong on a *credential* route is a small oracle for free.
            return Problem(
                StatusCodes.Status400BadRequest,
                GatewayProblem.Codes.ValidationFailed,
                "An email address and a password are required.");
        }

        CredentialCheckResult result;
        try
        {
            result = await identity.CheckCredentialsAsync(request.Email, request.Password, cancellationToken);
        }
        catch (Exception exception) when (exception is IdentityApiException or HttpRequestException)
        {
            LogSignInUnavailable(logger, exception);

            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                GatewayProblem.Codes.IdentityUnavailable,
                "Sign-in is unavailable. Try again shortly.");
        }

        if (!result.Succeeded)
        {
            // The distinction between a wrong password, a lockout and an unconfirmed staff address stops here.
            // It is logged, because an operator needs it; the browser gets one answer, because a browser given
            // three can enumerate accounts (ADR 0023). The API wrote the audit row.
            LogSignInRefused(logger, result.FailureCode ?? "unknown");

            return Problem(
                StatusCodes.Status401Unauthorized,
                GatewayProblem.Codes.CredentialsInvalid,
                "Check your email address and password, then try again.");
        }

        var ticket = await sessions.SignInAsync(context, result.User!, cancellationToken);

        return Results.Ok(new SessionResponse(ticket.DisplayName, ticket.Roles));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context, ISessionManager sessions, CancellationToken cancellationToken)
    {
        await sessions.SignOutAsync(context, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutEverywhereAsync(
        HttpContext context,
        ISessionManager sessions,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // The policy on the route guarantees a session, and a session principal always carries a subject.
        var userId = context.User.FindFirst(InternalTokenDefaults.SubjectClaim)!.Value;

        try
        {
            await sessions.SignOutEverywhereAsync(context, userId, cancellationToken);
        }
        catch (Exception exception) when (exception is IdentityApiException or HttpRequestException)
        {
            // This session is deliberately left intact: the promise is "every session ends", and ending only the
            // one that asked while the others live on is worse than refusing, because the person would believe
            // the opposite.
            LogRevocationUnavailable(loggerFactory.CreateLogger(typeof(BffEndpoints).FullName!), exception);

            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                GatewayProblem.Codes.IdentityUnavailable,
                "Sessions could not be ended. Try again shortly.");
        }

        return Results.NoContent();
    }

    private static bool IsPlausible([NotNullWhen(true)] LoginRequest? request) =>
        request is not null
        && !string.IsNullOrWhiteSpace(request.Email)
        && !string.IsNullOrWhiteSpace(request.Password)

        // The same ceilings the API's validator applies, so a body it would refuse is refused before a password
        // hash is spent on it.
        && request.Email.Length <= 256
        && request.Password.Length <= 256;

    private static GatewayProblemResult Problem(int status, string code, string? title = null) =>
        new(status, code, title);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Sign-in could not be attempted: the internal API did not answer.")]
    private static partial void LogSignInUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in refused: {FailureCode}.")]
    private static partial void LogSignInRefused(ILogger logger, string failureCode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Sessions could not be revoked: the internal API did not answer.")]
    private static partial void LogRevocationUnavailable(ILogger logger, Exception exception);

    /// <summary>
    /// Writes the gateway's <c>ProblemDetails</c> envelope, so a <c>/bff</c> failure looks exactly like every
    /// other failure from this origin — same <c>code</c>, same <c>correlationId</c>.
    /// </summary>
    private sealed record GatewayProblemResult(
        int Status, string Code, string? Title, IReadOnlyDictionary<string, string[]>? Errors = null) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext) => Title is null
            ? GatewayProblem.WriteAsync(httpContext, Status)
            : GatewayProblem.WriteAsync(httpContext, Status, Code, Title, Errors);
    }
}

/// <summary>
/// A sign-in attempt. The password exists on this record and nowhere else: it is never logged, never put on a
/// ticket and never forwarded.
/// </summary>
internal sealed record LoginRequest(string Email, string Password);

/// <summary>What <c>/bff/session</c> and a successful <c>/bff/login</c> return, and the whole of it.</summary>
internal sealed record SessionResponse(string DisplayName, IReadOnlyList<string> Roles);
