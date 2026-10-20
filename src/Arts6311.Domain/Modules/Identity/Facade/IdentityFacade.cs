using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Identity.Business;
using Arts6311.Domain.Modules.Identity.Managers.ServiceModels;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Identity.Facade;

/// <summary>
/// Validates shape, then calls Business once. Nothing here is cached: a credential check and a session
/// revalidation must both see the current row, or a revoked session would outlive its revocation.
/// </summary>
internal sealed class IdentityFacade(
    IIdentityBusiness business,
    IValidator<CredentialCheckViewModel> credentialValidator,
    IValidator<SessionValidationViewModel> sessionValidator,
    IValidator<SessionRevocationViewModel> revocationValidator,
    IValidator<PasswordResetRequestViewModel> resetRequestValidator,
    IValidator<PasswordResetCompletionViewModel> resetValidator,
    IValidator<EmailConfirmationCompletionViewModel> confirmationValidator,
    IValidator<EmailConfirmationRequestViewModel> confirmationRequestValidator) : IIdentityFacade
{
    public async Task<OperationResult<UserSummaryServiceModel>> CheckCredentialsAsync(
        CredentialCheckViewModel request, CancellationToken cancellationToken)
    {
        if (await credentialValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<UserSummaryServiceModel>(error);
        }

        return await business.CheckCredentialsAsync(request, cancellationToken);
    }

    public async Task<OperationResult<SessionValidationServiceModel>> ValidateSessionAsync(
        SessionValidationViewModel request, CancellationToken cancellationToken)
    {
        if (await sessionValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<SessionValidationServiceModel>(error);
        }

        return OperationResult.Success(await business.ValidateSessionAsync(request, cancellationToken));
    }

    public async Task<OperationResult> RevokeSessionsAsync(
        SessionRevocationViewModel request, CancellationToken cancellationToken)
    {
        if (await revocationValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        return await business.RevokeSessionsAsync(request, cancellationToken);
    }

    public async Task<OperationResult> RequestPasswordResetAsync(
        PasswordResetRequestViewModel request, CancellationToken cancellationToken)
    {
        if (await resetRequestValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        await business.RequestPasswordResetAsync(request.Email, cancellationToken);

        // Always success: the caller learns nothing about whether the address is registered.
        return OperationResult.Success();
    }

    public async Task<OperationResult> ResetPasswordAsync(
        PasswordResetCompletionViewModel request, CancellationToken cancellationToken)
    {
        if (await resetValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        return await business.ResetPasswordAsync(request, cancellationToken);
    }

    public async Task<OperationResult> ConfirmEmailAsync(
        EmailConfirmationCompletionViewModel request, CancellationToken cancellationToken)
    {
        if (await confirmationValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        return await business.ConfirmEmailAsync(request, cancellationToken);
    }

    public async Task<OperationResult> RequestEmailConfirmationAsync(
        string userId, CancellationToken cancellationToken)
    {
        await business.RequestEmailConfirmationAsync(userId, cancellationToken);
        return OperationResult.Success();
    }

    public async Task<OperationResult> ResendEmailConfirmationAsync(
        EmailConfirmationRequestViewModel request, CancellationToken cancellationToken)
    {
        if (await confirmationRequestValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure(error);
        }

        await business.ResendEmailConfirmationAsync(request.Email, cancellationToken);

        // Always success, as for a reset request: registered, unknown and already confirmed all look the same.
        return OperationResult.Success();
    }

    public Task<EmailTokenServiceModel?> GetPasswordResetTokenAsync(
        string userId, CancellationToken cancellationToken) =>
        business.GetPasswordResetTokenAsync(userId, cancellationToken);

    public Task<EmailTokenServiceModel?> GetEmailConfirmationTokenAsync(
        string userId, CancellationToken cancellationToken) =>
        business.GetEmailConfirmationTokenAsync(userId, cancellationToken);
}
