using System.Diagnostics.CodeAnalysis;
using Arts6311.Gateway.Edge;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Gateway.Session;

/// <summary>
/// The emailed-link endpoints: <c>/bff/forgot-password</c>, <c>/bff/reset-password</c> and <c>/bff/confirm-email</c>
/// (auth.md, ADR 0028). The site's pages (2.2b) read the user id and token from <c>location.hash</c> and POST them
/// here; the token is never in a URL.
/// </summary>
/// <remarks>
/// <para>
/// Antiforgery comes from the edge middleware like every other unsafe request, and the group's filter makes every
/// answer <c>no-store</c>. All three share the <c>bff-account</c> rate limit — separate from <c>bff-login</c>, so a
/// flood of reset requests cannot spend the sign-in budget.
/// </para>
/// <para>
/// <strong>None of these may enumerate accounts.</strong> <c>forgot-password</c> answers 202 before anything that
/// knows the answer has run; the other two answer one <c>auth.token_invalid</c> for every unusable link.
/// </para>
/// </remarks>
internal static partial class BffEndpoints
{
    /// <summary>
    /// The ceilings the API's validators apply (<c>PasswordResetCompletionValidator</c>), mirrored so a body the API
    /// would refuse is refused before the call. <c>SessionContractTests</c> holds them together.
    /// </summary>
    internal static class AccountLimits
    {
        public const int EmailMaxLength = 256;
        public const int UserIdMaxLength = 450;
        public const int TokenMaxLength = 2048;
        public const int PasswordMaxLength = 256;
    }

    private static void MapAccount(RouteGroupBuilder bff)
    {
        bff.MapPost("/forgot-password", ForgotPasswordAsync)
            .RequireRateLimiting(RateLimitExtensions.Policies.BffAccount);

        bff.MapPost("/reset-password", ResetPasswordAsync)
            .RequireRateLimiting(RateLimitExtensions.Policies.BffAccount);

        bff.MapPost("/confirm-email", ConfirmEmailAsync)
            .RequireRateLimiting(RateLimitExtensions.Policies.BffAccount);
    }

    /// <summary>
    /// Always 202, with no body, for a registered address and an unknown one alike — and at the same speed, because
    /// the API is called after this returns. Only a missing or oversized address is refused, which says nothing
    /// about any account.
    /// </summary>
    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest? request,
        IPasswordResetRequests requests,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || request.Email.Length > AccountLimits.EmailMaxLength)
        {
            return Problem(
                StatusCodes.Status400BadRequest, GatewayProblem.Codes.ValidationFailed, "An email address is required.");
        }

        try
        {
            await requests.SubmitAsync(request.Email, cancellationToken);
        }
        catch (ArgumentException)
        {
            // Normalizing the address threw: it is not valid Unicode (a lone surrogate), so it cannot be anyone's
            // address. A shape failure like any other, not an outage.
            return Problem(
                StatusCodes.Status400BadRequest, GatewayProblem.Codes.ValidationFailed, "An email address is required.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The cooldown store is unreachable. That is true for every address at once, so answering it says
            // nothing about this one; answering 202 would promise an email nothing is going to send.
            LogResetRequestUnavailable(
                loggerFactory.CreateLogger(typeof(BffEndpoints).FullName!), exception.GetType().Name);

            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                GatewayProblem.Codes.IdentityUnavailable,
                "Password reset is unavailable. Try again shortly.");
        }

        return Results.Accepted();
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest? request,
        IIdentityApiClient identity,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsPlausible(request))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                GatewayProblem.Codes.ValidationFailed,
                "A reset link and a new password are required.");
        }

        AccountLinkResult result;
        try
        {
            result = await identity.CompletePasswordResetAsync(
                request.UserId, request.Token, request.NewPassword, cancellationToken);
        }
        catch (IdentityApiException exception)
        {
            LogAccountLinkUnavailable(loggerFactory.CreateLogger(typeof(BffEndpoints).FullName!), exception);

            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                GatewayProblem.Codes.IdentityUnavailable,
                "Your password could not be changed. Try again shortly.");
        }

        if (result.Succeeded)
        {
            // No session is created and none is ended here. Every live session for this person, this browser's
            // included, ends at its next revalidation because the API rotated the stamp; the site sends the
            // person on to /signin (2.2b).
            return Results.NoContent();
        }

        // Only reachable with a valid token (the API checks it first), so the policy's reasons are the link
        // holder's to see.
        if (result.FailureCode == GatewayProblem.Codes.PasswordRejected)
        {
            return new GatewayProblemResult(
                StatusCodes.Status400BadRequest,
                GatewayProblem.Codes.PasswordRejected,
                "That password cannot be used.",
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["newPassword"] = [.. result.PasswordErrors],
                });
        }

        return LinkRefused();
    }

    private static async Task<IResult> ConfirmEmailAsync(
        [FromBody] ConfirmEmailRequest? request,
        IIdentityApiClient identity,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsPlausible(request))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                GatewayProblem.Codes.ValidationFailed,
                "A confirmation link is required.");
        }

        AccountLinkResult result;
        try
        {
            result = await identity.ConfirmEmailAsync(request.UserId, request.Token, cancellationToken);
        }
        catch (IdentityApiException exception)
        {
            LogAccountLinkUnavailable(loggerFactory.CreateLogger(typeof(BffEndpoints).FullName!), exception);

            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                GatewayProblem.Codes.IdentityUnavailable,
                "Your address could not be confirmed. Try again shortly.");
        }

        return result.Succeeded ? Results.NoContent() : LinkRefused();
    }

    /// <summary>
    /// The one answer for a link that cannot be used: wrong, expired, already used, an unknown user, an address
    /// already confirmed. The API's audit row says which; the browser is not told.
    /// </summary>
    private static GatewayProblemResult LinkRefused() => Problem(
        StatusCodes.Status400BadRequest,
        GatewayProblem.Codes.TokenInvalid,
        "This link has expired or has already been used. Request a new one.");

    private static bool IsPlausible([NotNullWhen(true)] ResetPasswordRequest? request) =>
        request is not null
        && IsPlausibleLink(request.UserId, request.Token)
        && !string.IsNullOrEmpty(request.NewPassword)
        && request.NewPassword.Length <= AccountLimits.PasswordMaxLength;

    private static bool IsPlausible([NotNullWhen(true)] ConfirmEmailRequest? request) =>
        request is not null && IsPlausibleLink(request.UserId, request.Token);

    private static bool IsPlausibleLink(string? userId, string? token) =>
        !string.IsNullOrWhiteSpace(userId)
        && !string.IsNullOrWhiteSpace(token)
        && userId.Length <= AccountLimits.UserIdMaxLength
        && token.Length <= AccountLimits.TokenMaxLength;

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A password reset request could not be accepted: the cooldown store did not answer ({ExceptionType}).")]
    private static partial void LogResetRequestUnavailable(ILogger logger, string exceptionType);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An emailed link could not be used: the internal API did not answer.")]
    private static partial void LogAccountLinkUnavailable(ILogger logger, Exception exception);
}

/// <summary>A reset request. The address is on this record and in the queue, and nowhere else.</summary>
internal sealed record ForgotPasswordRequest(string Email)
{
    /// <summary>Nothing: the generated <c>ToString</c> would print the address.</summary>
    public override string ToString() => nameof(ForgotPasswordRequest);
}

/// <summary>
/// The user id and token from the emailed link's fragment, and the new password. Never logged, never forwarded
/// anywhere but the API's internal route.
/// </summary>
internal sealed record ResetPasswordRequest(string UserId, string Token, string NewPassword)
{
    /// <summary>The user id only; the generated <c>ToString</c> would print the token and the password.</summary>
    public override string ToString() => $"{nameof(ResetPasswordRequest)} {{ UserId = {UserId} }}";
}

/// <summary>The user id and token from the emailed confirmation link's fragment.</summary>
internal sealed record ConfirmEmailRequest(string UserId, string Token)
{
    /// <summary>The user id only; the generated <c>ToString</c> would print the token.</summary>
    public override string ToString() => $"{nameof(ConfirmEmailRequest)} {{ UserId = {UserId} }}";
}
