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
    IValidator<SessionValidationViewModel> sessionValidator) : IIdentityFacade
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

    public async Task<OperationResult> RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        await business.RequestPasswordResetAsync(email, cancellationToken);

        // Always success: the caller learns nothing about whether the address is registered.
        return OperationResult.Success();
    }

    public async Task<OperationResult> RequestEmailConfirmationAsync(
        string userId, CancellationToken cancellationToken)
    {
        await business.RequestEmailConfirmationAsync(userId, cancellationToken);
        return OperationResult.Success();
    }

    public Task<EmailTokenServiceModel?> GetPasswordResetTokenAsync(
        string userId, CancellationToken cancellationToken) =>
        business.GetPasswordResetTokenAsync(userId, cancellationToken);

    public Task<EmailTokenServiceModel?> GetEmailConfirmationTokenAsync(
        string userId, CancellationToken cancellationToken) =>
        business.GetEmailConfirmationTokenAsync(userId, cancellationToken);
}
