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

    Task<OperationResult> RevokeSessionsAsync(
        SessionRevocationViewModel request, CancellationToken cancellationToken);

    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    Task RequestEmailConfirmationAsync(string userId, CancellationToken cancellationToken);

    Task<EmailTokenServiceModel?> GetPasswordResetTokenAsync(string userId, CancellationToken cancellationToken);

    Task<EmailTokenServiceModel?> GetEmailConfirmationTokenAsync(string userId, CancellationToken cancellationToken);

    Task<OperationResult> ResetPasswordAsync(
        PasswordResetCompletionViewModel request, CancellationToken cancellationToken);

    Task<OperationResult> ConfirmEmailAsync(
        EmailConfirmationCompletionViewModel request, CancellationToken cancellationToken);
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
            await dataLayer.RecordAuditAsync(
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

            await dataLayer.RecordAuditAsync(
                user.Id, IdentityAudit.SignInLockedOut, "Sign-in refused: account locked out.", cancellationToken);

            return OperationResult.Failure<UserSummaryServiceModel>(OperationError.Of(
                IdentityErrorCodes.LockedOut, "This account is temporarily locked. Try again later."));
        }

        if (!await dataLayer.CheckPasswordAsync(user, request.Password, cancellationToken))
        {
            // Counts towards the lockout, which is what makes the threshold mean anything.
            await dataLayer.RecordFailedAccessAsync(user, cancellationToken);
            await dataLayer.RecordAuditAsync(
                user.Id, IdentityAudit.SignInFailed, "Sign-in failed: incorrect password.", cancellationToken);

            return Invalid();
        }

        var roles = await dataLayer.GetRolesAsync(user, cancellationToken);

        // Staff only. A member with an unconfirmed address can still use the member area; a member who never
        // confirms simply never gets email. Staff reach the CMS, so the address has to be theirs (auth.md).
        if (!user.EmailConfirmed && IdentityRoles.IsStaff(roles))
        {
            await dataLayer.RecordAuditAsync(
                user.Id,
                IdentityAudit.SignInFailed,
                "Sign-in refused: staff email not confirmed.",
                cancellationToken);

            return OperationResult.Failure<UserSummaryServiceModel>(OperationError.Of(
                IdentityErrorCodes.EmailUnconfirmed, "Confirm your email address before signing in."));
        }

        await dataLayer.ResetFailedAccessAsync(user, cancellationToken);
        await dataLayer.RecordAuditAsync(
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

    public async Task<OperationResult> RevokeSessionsAsync(
        SessionRevocationViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await dataLayer.FindByIdAsync(request.UserId, cancellationToken);

        // Not found is reported rather than swallowed. The caller is the gateway, holding a live session for this
        // person, so a missing row means the account was deleted under it — the gateway deletes its ticket either
        // way, and a silent success here would hide a real inconsistency. There is no enumeration concern: a
        // browser cannot reach this route (ADR 0022) and cannot choose the user id.
        if (user is null)
        {
            return OperationResult.Failure(OperationError.Of(
                ErrorCodes.NotFound, "There is no such account."));
        }

        await dataLayer.RevokeSessionsAsync(user, cancellationToken);

        return OperationResult.Success();
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

    public async Task<OperationResult> ResetPasswordAsync(
        PasswordResetCompletionViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // An unknown id gets the answer a bad token gets, and no audit row: there is no account to attach one to.
        // It is faster than a known id, which is no oracle worth closing: the id is a GUID that only ever arrives
        // in the emailed link, so there is nothing to guess (ADR 0028).
        var user = await dataLayer.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return LinkRefused();
        }

        // The token before the password policy, deliberately. Checked the other way round, a too-short password
        // would be refused with auth.password_rejected for staff and auth.token_invalid for members, and anyone
        // holding a user id but no link could learn which the account is.
        if (!await dataLayer.VerifyPasswordResetTokenAsync(user, request.Token, cancellationToken))
        {
            return await ResetRefusedAsync(user.Id, cancellationToken);
        }

        // The staff rule cannot be an IPasswordValidator (it needs the roles; StaffPasswordRule explains why), so it
        // is applied here, to the account being reset. Without it a staff reset could set eight characters, which
        // is Identity's floor (ADR 0023).
        var roles = await dataLayer.GetRolesAsync(user, cancellationToken);
        if (IdentityRoles.IsStaff(roles) && StaffPasswordRule.Violation(request.NewPassword) is { } violation)
        {
            return PasswordRejected([violation]);
        }

        // Identity verifies the token again inside this call, runs the 8-character floor and the bundled list for
        // everyone, and rotates the security stamp, which spends the link and ends every live session at its next
        // revalidation (auth.md "Revalidation"). Lockout is left alone: auth.md says it is never relaxed without
        // an ADR, and a reset is not one.
        var outcome = await dataLayer.ResetPasswordAsync(user, request.Token, request.NewPassword, cancellationToken);

        if (outcome.TokenRejected)
        {
            return await ResetRefusedAsync(user.Id, cancellationToken);
        }

        if (!outcome.Succeeded)
        {
            return PasswordRejected(outcome.PasswordErrors);
        }

        await dataLayer.RecordAuditAsync(
            user.Id,
            IdentityAudit.PasswordResetCompleted,
            "Password reset with an emailed link; security stamp rotated, so every session ends at revalidation.",
            cancellationToken);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ConfirmEmailAsync(
        EmailConfirmationCompletionViewModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await dataLayer.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return LinkRefused();
        }

        if (!await dataLayer.VerifyEmailConfirmationTokenAsync(user, request.Token, cancellationToken))
        {
            return await ConfirmationRefusedAsync(
                user.Id, "Email confirmation refused: the link was invalid or expired.", cancellationToken);
        }

        // What makes a confirmation link work once. Confirming does not rotate the stamp (that would sign out a
        // member who confirms while signed in), so the token itself stays verifiable for its hour, and this is the
        // check that spends it. It comes after the token, so only the link's holder learns the address is already
        // confirmed (ADR 0028).
        if (user.EmailConfirmed)
        {
            return await ConfirmationRefusedAsync(
                user.Id, "Email confirmation refused: the address was already confirmed.", cancellationToken);
        }

        if (!await dataLayer.ConfirmEmailAsync(user, request.Token, cancellationToken))
        {
            return await ConfirmationRefusedAsync(
                user.Id, "Email confirmation refused: the store did not accept the link.", cancellationToken);
        }

        await dataLayer.RecordAuditAsync(
            user.Id, IdentityAudit.EmailConfirmed, "Email address confirmed with an emailed link.", cancellationToken);

        return OperationResult.Success();
    }

    private async Task<OperationResult> ResetRefusedAsync(string userId, CancellationToken cancellationToken)
    {
        // Audited because, with no second factor, somebody trying links against a staff account is worth seeing
        // (ADR 0018). The gateway's bff-account rate limit bounds how fast this table can grow from it.
        await dataLayer.RecordAuditAsync(
            userId,
            IdentityAudit.PasswordResetRefused,
            "Password reset refused: the link was invalid, expired or already used.",
            cancellationToken);

        return LinkRefused();
    }

    private async Task<OperationResult> ConfirmationRefusedAsync(
        string userId, string summary, CancellationToken cancellationToken)
    {
        await dataLayer.RecordAuditAsync(userId, IdentityAudit.EmailConfirmationRefused, summary, cancellationToken);
        return LinkRefused();
    }

    /// <summary>
    /// The one answer for a link that cannot be used, whatever the reason: wrong, expired, already used, an unknown
    /// user, an address already confirmed. The audit row says which; the caller is not told (ADR 0028).
    /// </summary>
    private static OperationResult LinkRefused() =>
        OperationResult.Failure(OperationError.Of(
            IdentityErrorCodes.TokenInvalid, "This link has expired or has already been used."));

    /// <summary>Only ever reached with a valid token, so the reasons are the link holder's to see.</summary>
    private static OperationResult PasswordRejected(IReadOnlyList<string> reasons) =>
        OperationResult.Failure(new OperationError(
            IdentityErrorCodes.PasswordRejected,
            "That password cannot be used.",
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["newPassword"] = [.. reasons] }));

    /// <summary>
    /// The one answer for "no such account" and "wrong password". Distinguishing them here would enumerate
    /// accounts to the gateway and, through it, to anyone who can read a login response.
    /// </summary>
    private static OperationResult<UserSummaryServiceModel> Invalid() =>
        OperationResult.Failure<UserSummaryServiceModel>(OperationError.Of(
            IdentityErrorCodes.CredentialsInvalid, "That email address and password do not match an account."));
}
