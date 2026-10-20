using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers.ServiceModels;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;

namespace Arts6311.Tests.Api.Infrastructure;

/// <summary>
/// The Identity facade, for the internal endpoint tests. What is under test there is the edge — which token gets
/// in and what shape comes back; the rules themselves run against real Identity in <c>Identity/</c>.
/// </summary>
internal sealed class FakeIdentityFacade : IIdentityFacade
{
    /// <summary>Set to make every operation fail with this error, as the real facade would.</summary>
    public OperationError? Error { get; init; }

    public int CredentialCalls { get; private set; }

    public int RevokeCalls { get; private set; }

    public string? RevokedUserId { get; private set; }

    public PasswordResetRequestViewModel? ResetRequest { get; private set; }

    public EmailConfirmationRequestViewModel? ConfirmationRequest { get; private set; }

    public PasswordResetCompletionViewModel? ResetCompletion { get; private set; }

    public EmailConfirmationCompletionViewModel? Confirmation { get; private set; }

    public Task<OperationResult<UserSummaryServiceModel>> CheckCredentialsAsync(
        CredentialCheckViewModel request, CancellationToken cancellationToken)
    {
        CredentialCalls++;

        return Task.FromResult(Error is { } error
            ? OperationResult.Failure<UserSummaryServiceModel>(error)
            : OperationResult.Success(new UserSummaryServiceModel("user-1", "Someone", ["Editor"], "stamp")));
    }

    public Task<OperationResult<SessionValidationServiceModel>> ValidateSessionAsync(
        SessionValidationViewModel request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success(new SessionValidationServiceModel(Valid: true, ["Editor"])));

    public Task<OperationResult> RevokeSessionsAsync(
        SessionRevocationViewModel request, CancellationToken cancellationToken)
    {
        RevokeCalls++;
        RevokedUserId = request.UserId;

        return Task.FromResult(Outcome());
    }

    public Task<OperationResult> RequestPasswordResetAsync(
        PasswordResetRequestViewModel request, CancellationToken cancellationToken)
    {
        ResetRequest = request;
        return Task.FromResult(Outcome());
    }

    public Task<OperationResult> ResetPasswordAsync(
        PasswordResetCompletionViewModel request, CancellationToken cancellationToken)
    {
        ResetCompletion = request;
        return Task.FromResult(Outcome());
    }

    public Task<OperationResult> ConfirmEmailAsync(
        EmailConfirmationCompletionViewModel request, CancellationToken cancellationToken)
    {
        Confirmation = request;
        return Task.FromResult(Outcome());
    }

    public Task<OperationResult> RequestEmailConfirmationAsync(
        string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult> ResendEmailConfirmationAsync(
        EmailConfirmationRequestViewModel request, CancellationToken cancellationToken)
    {
        ConfirmationRequest = request;
        return Task.FromResult(Outcome());
    }

    public Task<EmailTokenServiceModel?> GetPasswordResetTokenAsync(
        string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<EmailTokenServiceModel?> GetEmailConfirmationTokenAsync(
        string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    private OperationResult Outcome() => Error is { } error ? OperationResult.Failure(error) : OperationResult.Success();
}
