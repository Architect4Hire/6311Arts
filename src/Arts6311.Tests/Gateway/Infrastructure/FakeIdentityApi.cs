using System.Collections.Concurrent;
using System.Threading.Channels;
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

    /// <summary>The answer the next reset or confirmation link gets, as the API would give it.</summary>
    public AccountLinkResult LinkResult { get; set; } = AccountLinkResult.Success;

    /// <summary>
    /// While set, a reset request waits on it — standing in for an API that is slow to answer, or never does. The
    /// test that the browser is answered first depends on it.
    /// </summary>
    public TaskCompletionSource? HoldResetRequests { get; set; }

    /// <summary>Every address the dispatcher passed on, in order. Written from its background loop.</summary>
    public ConcurrentQueue<string> ResetRequests { get; } = new();

    public int ResetCompletions { get; private set; }

    public int Confirmations { get; private set; }

    private readonly Channel<string> resetRequested = Channel.CreateUnbounded<string>();

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        // Recorded as it starts, not as it finishes, so a held request still counts as "the API was asked".
        ResetRequests.Enqueue(email);
        resetRequested.Writer.TryWrite(email);

        if (HoldResetRequests is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        ThrowIfUnavailable();
    }

    /// <summary>
    /// The next address the dispatcher passes on. The dispatcher drains its queue in order, so awaiting a later
    /// address is how a test proves an earlier one was never queued.
    /// </summary>
    public async Task<string> NextResetRequestAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        return await resetRequested.Reader.ReadAsync(timeout.Token);
    }

    /// <summary>Every address the dispatcher passed on for a new confirmation link, in order.</summary>
    public ConcurrentQueue<string> ConfirmationRequests { get; } = new();

    private readonly Channel<string> confirmationRequested = Channel.CreateUnbounded<string>();

    public Task RequestEmailConfirmationAsync(string email, CancellationToken cancellationToken)
    {
        ConfirmationRequests.Enqueue(email);
        confirmationRequested.Writer.TryWrite(email);
        ThrowIfUnavailable();

        return Task.CompletedTask;
    }

    /// <summary>The next address the dispatcher passes on for a confirmation link.</summary>
    public async Task<string> NextConfirmationRequestAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        return await confirmationRequested.Reader.ReadAsync(timeout.Token);
    }

    public Task<AccountLinkResult> CompletePasswordResetAsync(
        string userId, string token, string newPassword, CancellationToken cancellationToken)
    {
        ResetCompletions++;
        ThrowIfUnavailable();

        // What the API does on success: the stamp moves, and every ticket holding the old one ends at revalidation.
        if (LinkResult.Succeeded)
        {
            CurrentStamp = $"stamp-{Guid.NewGuid():N}";
        }

        return Task.FromResult(LinkResult);
    }

    public Task<AccountLinkResult> ConfirmEmailAsync(
        string userId, string token, CancellationToken cancellationToken)
    {
        Confirmations++;
        ThrowIfUnavailable();

        return Task.FromResult(LinkResult);
    }

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
        {
            throw new IdentityApiException("The internal API is unavailable in this test.");
        }
    }
}
