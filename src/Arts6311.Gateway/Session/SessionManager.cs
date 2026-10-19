using System.Security.Claims;
using Arts6311.Gateway.Edge;
using Microsoft.AspNetCore.Antiforgery;

namespace Arts6311.Gateway.Session;

/// <summary>
/// Creating and ending sessions: what the <c>/bff</c> endpoints do once the API has answered.
/// </summary>
/// <remarks>
/// It exists so the three endpoints that change who the caller is cannot get the sequence wrong. Each one has to
/// write the ticket, write the cookie, replace <c>HttpContext.User</c> and re-issue the antiforgery pair — and
/// that fourth step is the one that would be forgotten (see <see cref="ReissueAntiforgery"/>).
/// </remarks>
internal interface ISessionManager
{
    Task<SessionTicket> SignInAsync(HttpContext context, UserSummary user, CancellationToken cancellationToken);

    /// <summary>Ends this session only. Safe to call without one.</summary>
    Task SignOutAsync(HttpContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every session this person has: the API rotates the security stamp, then this ticket goes. The others
    /// die at their next revalidation, which is at most <c>RevalidationInterval</c> away.
    /// </summary>
    Task SignOutEverywhereAsync(HttpContext context, string userId, CancellationToken cancellationToken);
}

internal sealed partial class SessionManager(
    ISessionTicketStore tickets,
    IIdentityApiClient identity,
    IAntiforgery antiforgery,
    BrowserSessionOptions session,
    TimeProvider time,
    ILogger<SessionManager> logger) : ISessionManager
{
    public async Task<SessionTicket> SignInAsync(
        HttpContext context, UserSummary user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(user);

        var now = time.GetUtcNow();
        var lifetime = session.LifetimeFor(user.Roles);

        var ticket = new SessionTicket(
            SessionId: Guid.NewGuid().ToString("N"),
            user.UserId,
            user.DisplayName,
            user.Roles,
            user.SecurityStamp,
            CreatedAt: now,
            AbsoluteExpiresAt: now + lifetime.AbsoluteTimeout,
            LastValidatedAt: now);

        // A signing-in request may already carry a cookie for an older ticket. A new ticket key is minted rather
        // than the old one reused, so the value a thief may hold cannot survive a fresh sign-in, and the previous
        // ticket is deleted rather than left to idle out.
        if (SessionCookie.Read(context, session) is { } previous)
        {
            await tickets.RemoveAsync(previous, cancellationToken);
        }

        var ticketKey = await tickets.CreateAsync(ticket, lifetime.IdleTimeout, cancellationToken);
        SessionCookie.Issue(context, session, ticketKey);

        // Logged without the email address: who signed in is on the audit row the API wrote, and the session id
        // is what ties this line to the request's traces.
        LogSessionBegan(logger, ticket.SessionId);

        ReissueAntiforgery(context, SessionPrincipal.From(ticket));

        return ticket;
    }

    public async Task SignOutAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (SessionCookie.Read(context, session) is { } ticketKey)
        {
            // The ticket is deleted, not expired: this is what makes a copied cookie worthless the moment the
            // person signs out (auth.md).
            await tickets.RemoveAsync(ticketKey, cancellationToken);
        }

        SessionCookie.Clear(context, session);
        ReissueAntiforgery(context, new ClaimsPrincipal());
    }

    public async Task SignOutEverywhereAsync(
        HttpContext context, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The stamp first. If this throws, nothing has been promised: the caller still holds a working session
        // and gets an error, rather than losing this session while the others live on.
        await identity.RevokeSessionsAsync(userId, cancellationToken);

        await SignOutAsync(context, cancellationToken);
    }

    /// <summary>
    /// Re-issues the antiforgery pair for the identity the response leaves with.
    /// </summary>
    /// <remarks>
    /// <strong>Antiforgery tokens are bound to the signed-in identity.</strong> A token minted while anonymous
    /// does not validate once there is a session, and vice versa, so without this the first write after signing
    /// in would be refused with a 400 that looks like a CSRF bug. <c>HttpContext.User</c> is replaced first
    /// because that is what the token is built from.
    /// </remarks>
    private void ReissueAntiforgery(HttpContext context, ClaimsPrincipal user)
    {
        context.User = user;

        // GetAndStoreTokens writes the cookie-token cookie; AntiforgeryEndpoints writes the readable half.
        AntiforgeryCookie.Issue(context, antiforgery);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A session began: {SessionId}.")]
    private static partial void LogSessionBegan(ILogger logger, string sessionId);
}
