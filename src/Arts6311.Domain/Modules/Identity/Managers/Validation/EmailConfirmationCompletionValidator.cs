using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Identity.Managers.Validation;

internal sealed class EmailConfirmationCompletionValidator : AbstractValidator<EmailConfirmationCompletionViewModel>
{
    public EmailConfirmationCompletionValidator()
    {
        RuleFor(model => model.UserId).NotEmpty().MaximumLength(PasswordResetCompletionValidator.UserIdMaxLength);
        RuleFor(model => model.Token).NotEmpty().MaximumLength(PasswordResetCompletionValidator.TokenMaxLength);
    }
}
