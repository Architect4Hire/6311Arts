namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// The audit vocabulary this module writes. Staff sign-ins, successful and failed, are audited because there is
/// no second factor to fall back on (auth.md, ADR 0018).
/// </summary>
internal static class IdentityAudit
{
    public const string UserResourceType = "identity.user";

    public const string SignInSucceeded = "identity.sign-in-succeeded";
    public const string SignInFailed = "identity.sign-in-failed";
    public const string SignInLockedOut = "identity.sign-in-locked-out";

    /// <summary>"Log out everywhere": the security stamp was rotated, so every live ticket dies.</summary>
    public const string SessionsRevoked = "identity.sessions-revoked";

    public const string PasswordResetRequested = "identity.password-reset-requested";
    public const string EmailConfirmationRequested = "identity.email-confirmation-requested";

    /// <summary>A new password was set with an emailed link; the security stamp moved, so every session ends.</summary>
    public const string PasswordResetCompleted = "identity.password-reset-completed";

    /// <summary>A reset link was refused for an account that exists. Written only then: an unknown id has no row to point at.</summary>
    public const string PasswordResetRefused = "identity.password-reset-refused";

    public const string EmailConfirmed = "identity.email-confirmed";

    public const string EmailConfirmationRefused = "identity.email-confirmation-refused";
    public const string AdminSeeded = "identity.admin-seeded";
}
