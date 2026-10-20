namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// What became of a password reset at the store, in this module's terms rather than Identity's
/// <c>IdentityResult</c>, so Business never reads Identity's error codes.
/// </summary>
/// <param name="TokenRejected">
/// The token did not verify, or another use of it won a race to the row. Either way the link is spent.
/// </param>
/// <param name="PasswordErrors">Why the password policy refused it, worded for the person choosing one.</param>
internal sealed record PasswordResetOutcome(bool TokenRejected, IReadOnlyList<string> PasswordErrors)
{
    public static readonly PasswordResetOutcome Success = new(false, []);

    public static readonly PasswordResetOutcome Rejected = new(true, []);

    public bool Succeeded => !TokenRejected && PasswordErrors.Count == 0;
}
