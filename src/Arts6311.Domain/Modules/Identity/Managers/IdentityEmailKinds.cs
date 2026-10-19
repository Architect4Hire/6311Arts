namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// The email kinds this module owns, as used in <c>EmailSendRequested.Kind</c>. Each has a composer registered
/// under the same string.
/// </summary>
public static class IdentityEmailKinds
{
    public const string EmailConfirmation = "identity.email-confirmation";

    public const string PasswordReset = "identity.password-reset";
}
