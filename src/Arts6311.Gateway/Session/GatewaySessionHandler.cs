using System.Text.Encodings.Web;
using Arts6311.Gateway.Edge;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Arts6311.Gateway.Session;

/// <summary>
/// Reads the session cookie, loads the server-side ticket and builds the principal (<c>auth.md</c> "Browser
/// session"). The whole of what being signed in means at the edge.
/// </summary>
/// <remarks>
/// <para>
/// Four things happen on every authenticated request, in this order: the absolute limit is checked, the security
/// stamp is revalidated if it is due, the roles are refreshed from that answer, and the idle window is restarted.
/// The idle window is the ticket's time to live in the store, so "restarted" is one <c>EXPIRE</c> — a session
/// that stops being used expires on its own, with no sweep anywhere.
/// </para>
/// <para>
/// <strong>An unreadable or missing ticket is not an error.</strong> It produces no principal, and the pipeline
/// then treats the request as anonymous: <c>/admin</c> and <c>/api/v1/cms/**</c> refuse it by policy, and
/// everything else carries no internal token, which is what the API's <c>[AllowAnonymous]</c> routes expect
/// (ADR 0022).
/// </para>
/// </remarks>
internal sealed partial class GatewaySessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISessionTicketStore tickets,
    IIdentityApiClient identity,
    BrowserSessionOptions session,
    TimeProvider time) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (SessionCookie.Read(Context, session) is not { } ticketKey)
        {
            return AuthenticateResult.NoResult();
        }

        if (await tickets.GetAsync(ticketKey, Context.RequestAborted) is not { } ticket)
        {
            // The cookie outlived its ticket: a logout elsewhere, a revocation, or the idle window closing. Clear
            // it so the browser stops presenting a reference to nothing.
            SessionCookie.Clear(Context, session);
            return AuthenticateResult.NoResult();
        }

        var now = time.GetUtcNow();

        if (ticket.AbsoluteExpiresAt <= now)
        {
            await EndAsync(ticketKey, "absolute lifetime reached");
            return AuthenticateResult.NoResult();
        }

        var lifetime = session.LifetimeFor(ticket.Roles);

        if (now - ticket.LastValidatedAt >= session.RevalidationInterval)
        {
            SessionValidationResult validation;
            try
            {
                validation = await identity.ValidateSessionAsync(
                    ticket.UserId, ticket.SecurityStamp, Context.RequestAborted);
            }
            catch (Exception exception) when (exception is IdentityApiException or HttpRequestException)
            {
                // The API could not answer, so we do not know whether this session is still good. The request is
                // refused — the safe direction — but the ticket is left alone: deleting it would turn an API
                // restart into signing everybody out, and the answer may well be "still valid" a second later.
                LogRevalidationUnavailable(Logger, exception);

                return AuthenticateResult.NoResult();
            }

            if (!validation.Valid)
            {
                // The stamp moved: a password reset, a lockout, or "log out everywhere" (auth.md "Revalidation").
                await EndAsync(ticketKey, "security stamp no longer current");
                return AuthenticateResult.NoResult();
            }

            // Roles as they stand now, so a role change takes effect here rather than at the next sign-in. They
            // also decide the lifetimes, so this is read back before the window is set.
            lifetime = session.LifetimeFor(validation.Roles);

            ticket = ticket with
            {
                Roles = validation.Roles,
                LastValidatedAt = now,

                // Someone promoted to staff mid-session must come under the staff limits, not keep the member
                // deadline they signed in with — the 8-hour cap is a compensating control for having no second
                // factor (ADR 0018). Only ever brought forward: losing a staff role does not extend a session.
                AbsoluteExpiresAt = Earlier(
                    ticket.AbsoluteExpiresAt, ticket.CreatedAt + lifetime.AbsoluteTimeout),
            };

            if (ticket.AbsoluteExpiresAt <= now)
            {
                // The new, shorter limit is already behind us.
                await EndAsync(ticketKey, "absolute lifetime reached under new roles");
                return AuthenticateResult.NoResult();
            }

            if (!await tickets.UpdateAsync(
                ticketKey, ticket, TimeToLive(ticket, lifetime, now), Context.RequestAborted))
            {
                // Deleted while we were revalidating it — a logout elsewhere. The write did not recreate it.
                SessionCookie.Clear(Context, session);
                return AuthenticateResult.NoResult();
            }
        }
        else if (!await tickets.TouchAsync(
            ticketKey, TimeToLive(ticket, lifetime, now), Context.RequestAborted))
        {
            // It expired between the read above and this write. Rare, and it means the same thing as a miss.
            SessionCookie.Clear(Context, session);
            return AuthenticateResult.NoResult();
        }

        return AuthenticateResult.Success(
            new AuthenticationTicket(SessionPrincipal.From(ticket), Scheme.Name));
    }

    /// <summary>
    /// What the ticket's time to live should be from here: the idle window, never past the absolute deadline. The
    /// cap is what keeps an expired ticket from lingering in the store to be read and rejected.
    /// </summary>
    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) =>
        left < right ? left : right;

    private static TimeSpan TimeToLive(SessionTicket ticket, BrowserSessionLifetimeOptions lifetime, DateTimeOffset now)
    {
        var untilAbsolute = ticket.AbsoluteExpiresAt - now;

        return untilAbsolute < lifetime.IdleTimeout ? untilAbsolute : lifetime.IdleTimeout;
    }

    private async Task EndAsync(string ticketKey, string reason)
    {
        await tickets.RemoveAsync(ticketKey, Context.RequestAborted);
        SessionCookie.Clear(Context, session);

        // The session id is not in scope here — the ticket may be unreadable — and the user id is deliberately
        // not logged for an ended session: the reason is what an operator needs.
        LogSessionEnded(Logger, reason);
    }

    /// <summary>
    /// No session, and something needs one. A browser navigation is redirected to the login page; everything else
    /// — an SPA call, a fetch, a proxied API request — gets a 401 <c>ProblemDetails</c> it can act on.
    /// </summary>
    /// <remarks>
    /// Both are required by <c>auth.md</c>'s test list ("CMS route without session 401/302"), and the difference
    /// is the request, not the route: a person who typed <c>/admin</c> wants a login form, and a background fetch
    /// wants a status code rather than an HTML page in its JSON parser.
    /// </remarks>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!WantsHtmlPage())
        {
            return GatewayProblem.WriteAsync(Context, StatusCodes.Status401Unauthorized);
        }

        // The return path is this request's own, so there is nothing a caller could point it at: no open
        // redirect, whatever is in the query string.
        var returnUrl = Context.Request.Path + Context.Request.QueryString;

        Context.Response.Redirect(
            $"{session.LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Signed in, but not as someone this route is for. Never a redirect: sending a signed-in person to a login
    /// page says "your session failed" when what happened is "your account may not do this".
    /// </summary>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        GatewayProblem.WriteAsync(Context, StatusCodes.Status403Forbidden);

    private bool WantsHtmlPage() =>
        (HttpMethods.IsGet(Context.Request.Method) || HttpMethods.IsHead(Context.Request.Method))
        && Context.Request.Headers.Accept.Any(value =>
            value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A session could not be revalidated, so it was refused for this request.")]
    private static partial void LogRevalidationUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "A session ended: {Reason}.")]
    private static partial void LogSessionEnded(ILogger logger, string reason);
}
