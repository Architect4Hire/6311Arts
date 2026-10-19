using Arts6311.Domain.Modules.Identity.Data.Entities;

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

    /// <summary>Audits the sign-in outcome. One save; no Identity write is involved.</summary>
    Task RecordSignInOutcomeAsync(string? userId, string action, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Stages the email message and its audit row, then makes the single save that commits them together.
    /// </summary>
    Task RequestEmailAsync(string userId, string kind, string auditAction, string summary, CancellationToken cancellationToken);

    Task<string> GeneratePasswordResetTokenAsync(ArtsUser user, CancellationToken cancellationToken);

    Task<string> GenerateEmailConfirmationTokenAsync(ArtsUser user, CancellationToken cancellationToken);
}
