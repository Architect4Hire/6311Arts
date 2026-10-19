namespace Arts6311.Gateway.Session;

/// <summary>
/// The three internal API routes the BFF calls (<c>auth.md</c> "The internal routes the BFF calls"). The only
/// outbound calls the gateway makes on its own behalf.
/// </summary>
internal interface IIdentityApiClient
{
    /// <summary>
    /// Checks an email and password. A failure carries the API's code — which the caller must <strong>not</strong>
    /// pass to a browser (ADR 0023).
    /// </summary>
    Task<CredentialCheckResult> CheckCredentialsAsync(
        string email, string password, CancellationToken cancellationToken);

    /// <summary>Whether a ticket's security stamp still matches, and the roles as they stand now.</summary>
    Task<SessionValidationResult> ValidateSessionAsync(
        string userId, string securityStamp, CancellationToken cancellationToken);

    /// <summary>Rotates the security stamp, ending every session this person has.</summary>
    Task<bool> RevokeSessionsAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// The API's <c>UserSummaryServiceModel</c>, as the gateway reads it. Mirrored rather than referenced: the
/// gateway holds no domain code (ADR 0022), and <c>SessionContractTests</c> checks the two shapes agree.
/// </summary>
internal sealed record UserSummary(
    string UserId, string DisplayName, IReadOnlyList<string> Roles, string SecurityStamp);

/// <param name="FailureCode">
/// <c>auth.credentials_invalid</c>, <c>auth.locked_out</c> or <c>auth.email_unconfirmed</c>. For the log and
/// nothing else: a browser told which one can enumerate accounts.
/// </param>
internal sealed record CredentialCheckResult(UserSummary? User, string? FailureCode)
{
    public bool Succeeded => User is not null;
}

internal sealed record SessionValidationResult(bool Valid, IReadOnlyList<string> Roles);
