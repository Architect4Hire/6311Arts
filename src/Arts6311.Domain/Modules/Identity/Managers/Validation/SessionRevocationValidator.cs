using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Identity.Managers.Validation;

internal sealed class SessionRevocationValidator : AbstractValidator<SessionRevocationViewModel>
{
    public SessionRevocationValidator() =>

        // 450 is the Identity key column's length, as SessionValidationValidator already assumes.
        RuleFor(model => model.UserId).NotEmpty().MaximumLength(450);
}
