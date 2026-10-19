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
    public const string PasswordResetRequested = "identity.password-reset-requested";
    public const string EmailConfirmationRequested = "identity.email-confirmation-requested";
    public const string AdminSeeded = "identity.admin-seeded";
}
