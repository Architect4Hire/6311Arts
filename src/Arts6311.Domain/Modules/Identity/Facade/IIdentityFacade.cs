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
    /// Stages a password-reset email. Succeeds whether or not the address is registered, so it cannot be used to
    /// test for an account. No route calls this yet: 1.10's <c>/bff/forgot-password</c> is its caller.
    /// </summary>
    Task<OperationResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

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
