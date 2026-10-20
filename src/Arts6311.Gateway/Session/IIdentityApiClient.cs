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

    /// <summary>
    /// Asks the API to email a reset link if the address is registered. Called from the background dispatcher,
    /// never from the request: the browser has its answer before this starts (ADR 0028).
    /// </summary>
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);

    /// <summary>Sets a new password with an emailed link's user id and token.</summary>
    Task<AccountLinkResult> CompletePasswordResetAsync(
        string userId, string token, string newPassword, CancellationToken cancellationToken);

    /// <summary>Confirms an address with an emailed link's user id and token.</summary>
    Task<AccountLinkResult> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken);
}

/// <summary>What became of an emailed link: used, or refused with the API's code.</summary>
/// <param name="FailureCode">
/// <c>auth.token_invalid</c>, <c>auth.password_rejected</c> or <c>validation.failed</c>. Safe to pass on, unlike the
/// credential codes: every unusable link shares the first, and the second needs a valid token (ADR 0028).
/// </param>
/// <param name="PasswordErrors">The policy's reasons, for <c>auth.password_rejected</c> only.</param>
internal sealed record AccountLinkResult(string? FailureCode, IReadOnlyList<string> PasswordErrors)
{
    public static readonly AccountLinkResult Success = new(null, []);

    public bool Succeeded => FailureCode is null;
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
