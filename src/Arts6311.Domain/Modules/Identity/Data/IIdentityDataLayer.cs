using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Managers;

namespace Arts6311.Domain.Modules.Identity.Data;

/// <summary>The persistence operations behind the Identity module's Business layer.</summary>
internal interface IIdentityDataLayer
{
    Task<ArtsUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<ArtsUser?> FindByIdAsync(string userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetRolesAsync(ArtsUser user, CancellationToken cancellationToken);

    Task<bool> IsLockedOutAsync(ArtsUser user, CancellationToken cancellationToken);

    /// <summary>Verifies the password without touching the lockout counters.</summary>
    Task<bool> CheckPasswordAsync(ArtsUser user, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Burns one unit of CPU hashing against a throwaway hash. Called when no account matched, so that a
    /// missing user costs what a wrong password costs and the response time says nothing.
    /// </summary>
    void VerifyDummyPassword(string password);

    /// <summary>Counts a failure and locks the account once the threshold is reached.</summary>
    Task RecordFailedAccessAsync(ArtsUser user, CancellationToken cancellationToken);

    Task ResetFailedAccessAsync(ArtsUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Rotates the security stamp and audits it, which is what "log out everywhere" is made of: every live
    /// ticket holds the old stamp and fails its next revalidation (auth.md "Revalidation").
    /// </summary>
    Task RevokeSessionsAsync(ArtsUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Writes one audit row for this module — a sign-in outcome, a reset, a confirmation — in its own save. No
    /// Identity write is involved and nothing is staged with it.
    /// </summary>
    Task RecordAuditAsync(string? userId, string action, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Stages the email message and its audit row, then makes the single save that commits them together.
    /// </summary>
    Task RequestEmailAsync(string userId, string kind, string auditAction, string summary, CancellationToken cancellationToken);

    Task<string> GeneratePasswordResetTokenAsync(ArtsUser user, CancellationToken cancellationToken);

    Task<string> GenerateEmailConfirmationTokenAsync(ArtsUser user, CancellationToken cancellationToken);

    /// <summary>Whether a reset token is good for this user now. Changes nothing.</summary>
    Task<bool> VerifyPasswordResetTokenAsync(ArtsUser user, string token, CancellationToken cancellationToken);

    /// <summary>Whether a confirmation token is good for this user now. Changes nothing.</summary>
    Task<bool> VerifyEmailConfirmationTokenAsync(ArtsUser user, string token, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a new password with a reset token. Identity verifies the token again, runs every password validator
    /// (the 8-character floor and the bundled list) and rotates the security stamp, in its own save.
    /// </summary>
    Task<PasswordResetOutcome> ResetPasswordAsync(
        ArtsUser user, string token, string newPassword, CancellationToken cancellationToken);

    /// <summary>Marks the address confirmed with a confirmation token. <c>false</c> when the store refused it.</summary>
    Task<bool> ConfirmEmailAsync(ArtsUser user, string token, CancellationToken cancellationToken);
}
