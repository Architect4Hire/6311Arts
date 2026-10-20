using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Managers.ServiceModels;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Identity.Facade;

/// <summary>
/// The Identity module's boundary, used by the API's internal controllers and by this module's email composers.
/// </summary>
public interface IIdentityFacade
{
    /// <summary>
    /// Checks a password and returns what the gateway needs to build a session. Failures carry one of
    /// <c>IdentityErrorCodes</c>; the gateway must not pass those codes through to a browser (ADR 0023).
    /// </summary>
    Task<OperationResult<UserSummaryServiceModel>> CheckCredentialsAsync(
        CredentialCheckViewModel request, CancellationToken cancellationToken);

    /// <summary>Whether a session ticket is still good, and the roles as they stand now.</summary>
    Task<OperationResult<SessionValidationServiceModel>> ValidateSessionAsync(
        SessionValidationViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Ends every session this person has, by rotating their security stamp. The caller's own ticket is deleted
    /// by the gateway; the others fail their next revalidation (auth.md "Log out everywhere").
    /// </summary>
    Task<OperationResult> RevokeSessionsAsync(
        SessionRevocationViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a password-reset email. Succeeds whether or not the address is registered, so it cannot be used to
    /// test for an account. Only a malformed request fails, and that does not depend on any account.
    /// </summary>
    /// <remarks>
    /// Its response time still differs between a known and an unknown address. That is closed at the gateway, which
    /// answers the browser before this is called (ADR 0028), not here.
    /// </remarks>
    Task<OperationResult> RequestPasswordResetAsync(
        PasswordResetRequestViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a new password with an emailed reset link. The token is checked first; then the staff rule for Admin and
    /// Editor accounts, and the 8-character floor and bundled list for everyone. Success rotates the security
    /// stamp, so the link is spent and every live session ends at its next revalidation. Failures are
    /// <c>auth.token_invalid</c> (one answer for every unusable link) or <c>auth.password_rejected</c> with field
    /// errors (ADR 0028).
    /// </summary>
    Task<OperationResult> ResetPasswordAsync(
        PasswordResetCompletionViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Confirms an address with an emailed link. Works once; any unusable link, including one for an address that
    /// is already confirmed, is <c>auth.token_invalid</c> (ADR 0028).
    /// </summary>
    Task<OperationResult> ConfirmEmailAsync(
        EmailConfirmationCompletionViewModel request, CancellationToken cancellationToken);

    /// <summary>Stages a confirmation email, or does nothing if the address is already confirmed.</summary>
    Task<OperationResult> RequestEmailConfirmationAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// The recipient and a freshly minted reset token, for the composer. <c>null</c> when there is nothing to
    /// send. Minted at send time: a token must never sit in an outbox payload.
    /// </summary>
    Task<EmailTokenServiceModel?> GetPasswordResetTokenAsync(string userId, CancellationToken cancellationToken);

    /// <summary>As above, for confirmation. <c>null</c> once the address is confirmed.</summary>
    Task<EmailTokenServiceModel?> GetEmailConfirmationTokenAsync(string userId, CancellationToken cancellationToken);
}
