using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Identity.Managers.Validation;

/// <summary>
/// Shape only, and nothing that depends on an account: present and plausibly sized. No format check, for the
/// reason <see cref="CredentialCheckValidator"/> gives — and an address we do not hold is not a validation error,
/// it is the same success as one we do.
/// </summary>
internal sealed class PasswordResetRequestValidator : AbstractValidator<PasswordResetRequestViewModel>
{
    public PasswordResetRequestValidator() =>
        RuleFor(model => model.Email).NotEmpty().MaximumLength(CredentialCheckValidator.EmailMaxLength);
}
