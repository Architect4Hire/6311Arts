using Arts6311.Gateway.Session;

namespace Arts6311.Tests.Gateway.Infrastructure;

/// <summary>
/// The internal API, as the gateway sees it. What the real one decides is covered against real Identity in
/// <c>Identity/CredentialCheckTests</c>; what matters here is what the gateway does with each answer.
/// </summary>
internal sealed class FakeIdentityApi : IIdentityApiClient
{
    public UserSummary User { get; set; } =
        new("user-1", "Ada Editor", ["Editor"], "stamp-1");

    /// <summary>Set to refuse the next sign-in with this code, as the API would.</summary>
    public string? FailureCode { get; set; }

    /// <summary>The stamp the API considers current. Changing it is what "log out everywhere" does.</summary>
    public string CurrentStamp { get; set; } = "stamp-1";

    /// <summary>The roles revalidation reports, which is how a role change reaches a live session.</summary>
    public IReadOnlyList<string>? RolesOnRevalidation { get; set; }

    /// <summary>Set to make the API unreachable, the case that must not end a session.</summary>
    public bool Unavailable { get; set; }

    /// <summary>Runs inside a revalidation call, for the races that happen while the gateway is waiting.</summary>
    public Action? BeforeValidate { get; set; }

    public int CredentialCalls { get; private set; }

    public int ValidateCalls { get; private set; }

    public int RevokeCalls { get; private set; }

    public string? RevokedUserId { get; private set; }

    public Task<CredentialCheckResult> CheckCredentialsAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        CredentialCalls++;
        ThrowIfUnavailable();

        return Task.FromResult(FailureCode is { } code
            ? new CredentialCheckResult(null, code)
            : new CredentialCheckResult(User, null));
    }

    public Task<SessionValidationResult> ValidateSessionAsync(
        string userId, string securityStamp, CancellationToken cancellationToken)
    {
        ValidateCalls++;
        BeforeValidate?.Invoke();
        ThrowIfUnavailable();

        return Task.FromResult(string.Equals(securityStamp, CurrentStamp, StringComparison.Ordinal)
            ? new SessionValidationResult(true, RolesOnRevalidation ?? User.Roles)
            : new SessionValidationResult(false, []));
    }

    public Task<bool> RevokeSessionsAsync(string userId, CancellationToken cancellationToken)
    {
        RevokeCalls++;
        RevokedUserId = userId;
        ThrowIfUnavailable();

        // What the API does: the stamp moves, so every ticket holding the old one fails its next revalidation.
        CurrentStamp = $"stamp-{Guid.NewGuid():N}";

        return Task.FromResult(true);
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
        {
            throw new IdentityApiException("The internal API is unavailable in this test.");
        }
    }
}
