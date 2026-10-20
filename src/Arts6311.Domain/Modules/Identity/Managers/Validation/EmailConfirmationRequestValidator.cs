using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Identity.Managers.Validation;

/// <summary>
/// Shape only, exactly as <see cref="PasswordResetRequestValidator"/>: an address we do not hold, or one already
/// confirmed, is the same success as one we would write to.
/// </summary>
internal sealed class EmailConfirmationRequestValidator : AbstractValidator<EmailConfirmationRequestViewModel>
{
    public EmailConfirmationRequestValidator() =>
        RuleFor(model => model.Email).NotEmpty().MaximumLength(CredentialCheckValidator.EmailMaxLength);
}
