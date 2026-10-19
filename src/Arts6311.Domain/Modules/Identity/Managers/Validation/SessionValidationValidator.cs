using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Identity.Managers.Validation;

internal sealed class SessionValidationValidator : AbstractValidator<SessionValidationViewModel>
{
    public SessionValidationValidator()
    {
        RuleFor(model => model.UserId).NotEmpty().MaximumLength(450);
        RuleFor(model => model.SecurityStamp).NotEmpty().MaximumLength(256);
    }
}
