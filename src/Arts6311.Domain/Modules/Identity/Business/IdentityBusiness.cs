using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Data;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Identity.Managers.ServiceModels;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Identity.Business;

internal interface IIdentityBusiness
{
    Task<OperationResult<UserSummaryServiceModel>> CheckCredentialsAsync(
        CredentialCheckViewModel request, CancellationToken cancellationToken);

    Task<SessionValidationServiceModel> ValidateSessionAsync(
        SessionValidationViewModel request, CancellationToken cancellationToken);

    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    Task RequestEmailConfirmationAsync(string userId, CancellationToken cancellationToken);

    Task<EmailTokenServiceModel?> GetPasswordResetTokenAsync(string userId, CancellationToken cancellationToken);

    Task<EmailTokenServiceModel?> GetEmailConfirmationTokenAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// The rules of signing in (auth.md, ADR 0018). With no second factor, these are the whole defence: a lockout
/// that actually counts, a confirmed address for staff, and failures that give nothing away.
/// </summary>
internal sealed class IdentityBusiness(IIdentityDataLayer dataLayer) : IIdentityBusiness
{
    public async Task<OperationResult<UserSummaryServiceModel>> CheckCredentialsAsync(
        CredentialCheckViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await dataLayer.FindByEmailAsync(request.Email, cancellationToken);

        if (user is null)
        {
            // Hash anyway. Returning here without hashing would make "no such account" measurably faster than
            // "wrong password", which is an enumeration oracle that no amount of identical wording hides.
            dataLayer.VerifyDummyPassword(request.Password);
            await dataLayer.RecordSignInOutcomeAsync(
                null, IdentityAudit.SignInFailed, "Sign-in failed: no matching account.", cancellationToken);

            return Invalid();
        }

        // Checked before the password: a locked account must not be a way to keep testing guesses, and the
        // attempt still costs the attacker a lockout window.
        if (await dataLayer.IsLockedOutAsync(user, cancellationToken))
        {
            // Hash anyway, for the same reason the missing-account path does. Returning here without hashing
            // would make a locked account answer measurably faster than an unknown address — so five bad guesses
            // at any address would reveal whether it exists, and would keep revealing it after 1.10 collapses
            // these codes into one browser response.
            dataLayer.VerifyDummyPassword(request.Password);

            await dataLayer.RecordSignInOutcomeAsync(
                user.Id, IdentityAudit.SignInLockedOut, "Sign-in refused: account locked out.", cancellationToken);

            return OperationResult.Failure<UserSummaryServiceModel>(OperationError.Of(
                IdentityErrorCodes.LockedOut, "This account is temporarily locked. Try again later."));
        }

        if (!await dataLayer.CheckPasswordAsync(user, request.Password, cancellationToken))
        {
            // Counts towards the lockout, which is what makes the threshold mean anything.
            await dataLayer.RecordFailedAccessAsync(user, cancellationToken);
            await dataLayer.RecordSignInOutcomeAsync(
                user.Id, IdentityAudit.SignInFailed, "Sign-in failed: incorrect password.", cancellationToken);

            return Invalid();
        }

        var roles = await dataLayer.GetRolesAsync(user, cancellationToken);

        // Staff only. A member with an unconfirmed address can still use the member area; a member who never
        // confirms simply never gets email. Staff reach the CMS, so the address has to be theirs (auth.md).
        if (!user.EmailConfirmed && IdentityRoles.IsStaff(roles))
        {
            await dataLayer.RecordSignInOutcomeAsync(
                user.Id,
                IdentityAudit.SignInFailed,
                "Sign-in refused: staff email not confirmed.",
                cancellationToken);

            return OperationResult.Failure<UserSummaryServiceModel>(OperationError.Of(
                IdentityErrorCodes.EmailUnconfirmed, "Confirm your email address before signing in."));
        }

        await dataLayer.ResetFailedAccessAsync(user, cancellationToken);
        await dataLayer.RecordSignInOutcomeAsync(
            user.Id, IdentityAudit.SignInSucceeded, "Sign-in succeeded.", cancellationToken);

        return OperationResult.Success(new UserSummaryServiceModel(
            user.Id, user.DisplayName, roles, user.SecurityStamp ?? string.Empty));
    }

    public async Task<SessionValidationServiceModel> ValidateSessionAsync(
        SessionValidationViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await dataLayer.FindByIdAsync(request.UserId, cancellationToken);

        // A moved stamp is the mechanism behind "a password reset ends sessions" and "log out everywhere": both
        // rotate it, and every live ticket fails its next revalidation (auth.md).
        if (user is null
            || !string.Equals(user.SecurityStamp, request.SecurityStamp, StringComparison.Ordinal)
            || await dataLayer.IsLockedOutAsync(user, cancellationToken))
        {
            return new SessionValidationServiceModel(Valid: false, []);
        }

        return new SessionValidationServiceModel(
            Valid: true, await dataLayer.GetRolesAsync(user, cancellationToken));
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var user = await dataLayer.FindByEmailAsync(email, cancellationToken);

        // Nothing staged and nothing recorded for an address we do not hold. The caller is told the same either
        // way, so a forgotten-password form cannot be used to test whether an address is registered (auth.md).
        if (user is null)
        {
            return;
        }

        await dataLayer.RequestEmailAsync(
            user.Id,
            IdentityEmailKinds.PasswordReset,
            IdentityAudit.PasswordResetRequested,
            "Password reset requested.",
            cancellationToken);
    }

    public async Task RequestEmailConfirmationAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await dataLayer.FindByIdAsync(userId, cancellationToken);

        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        await dataLayer.RequestEmailAsync(
            user.Id,
            IdentityEmailKinds.EmailConfirmation,
            IdentityAudit.EmailConfirmationRequested,
            "Email confirmation requested.",
            cancellationToken);
    }

    public async Task<EmailTokenServiceModel?> GetPasswordResetTokenAsync(
        string userId, CancellationToken cancellationToken)
    {
        var user = await dataLayer.FindByIdAsync(userId, cancellationToken);
        if (user is null || string.IsNullOrEmpty(user.Email))
        {
            return null;
        }

        return new EmailTokenServiceModel(
            user.Email,
            user.DisplayName,
            await dataLayer.GeneratePasswordResetTokenAsync(user, cancellationToken));
    }

    public async Task<EmailTokenServiceModel?> GetEmailConfirmationTokenAsync(
        string userId, CancellationToken cancellationToken)
    {
        var user = await dataLayer.FindByIdAsync(userId, cancellationToken);

        // Already confirmed: nothing to send. The composer turns this into a recorded skip rather than a retry.
        if (user is null || user.EmailConfirmed || string.IsNullOrEmpty(user.Email))
        {
            return null;
        }

        return new EmailTokenServiceModel(
            user.Email,
            user.DisplayName,
            await dataLayer.GenerateEmailConfirmationTokenAsync(user, cancellationToken));
    }

    /// <summary>
    /// The one answer for "no such account" and "wrong password". Distinguishing them here would enumerate
    /// accounts to the gateway and, through it, to anyone who can read a login response.
    /// </summary>
    private static OperationResult<UserSummaryServiceModel> Invalid() =>
        OperationResult.Failure<UserSummaryServiceModel>(OperationError.Of(
            IdentityErrorCodes.CredentialsInvalid, "That email address and password do not match an account."));
}
