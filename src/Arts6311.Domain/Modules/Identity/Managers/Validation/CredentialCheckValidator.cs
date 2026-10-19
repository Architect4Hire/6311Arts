using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Identity.Managers.Validation;

/// <summary>
/// Shape only (backend.md): that both fields are present and plausibly sized. Whether the credentials are
/// <em>correct</em> is Business's question, and must not be answerable from a validation message.
/// </summary>
internal sealed class CredentialCheckValidator : AbstractValidator<CredentialCheckViewModel>
{
    /// <summary>
    /// A ceiling on what is hashed. Without one, a megabyte-long password would make the hash itself a way to
    /// burn CPU on an unauthenticated route.
    /// </summary>
    public const int PasswordMaxLength = 256;

    public const int EmailMaxLength = 256;

    public CredentialCheckValidator()
    {
        RuleFor(model => model.Email).NotEmpty().MaximumLength(EmailMaxLength);

        // No minimum length and no format check here: both would reject a wrong guess with a *different* answer
        // than a correct-shaped wrong guess, which tells an attacker something about the policy.
        RuleFor(model => model.Password).NotEmpty().MaximumLength(PasswordMaxLength);
    }
}
