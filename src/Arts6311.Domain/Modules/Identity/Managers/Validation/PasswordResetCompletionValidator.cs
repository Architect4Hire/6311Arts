using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Identity.Managers.Validation;

/// <summary>
/// Shape only. The password rules are Business's, because one of them depends on the account's roles and none of
/// them may be answered before the token has been checked (ADR 0028).
/// </summary>
internal sealed class PasswordResetCompletionValidator : AbstractValidator<PasswordResetCompletionViewModel>
{
    /// <summary>
    /// A ceiling on what is handed to Data Protection to unprotect. A real token is a few hundred characters.
    /// </summary>
    public const int TokenMaxLength = 2048;

    /// <summary>The Identity key column's length, as the session validators already assume.</summary>
    public const int UserIdMaxLength = 450;

    public PasswordResetCompletionValidator()
    {
        RuleFor(model => model.UserId).NotEmpty().MaximumLength(UserIdMaxLength);
        RuleFor(model => model.Token).NotEmpty().MaximumLength(TokenMaxLength);

        // A ceiling on what is hashed, as on the credential route. No minimum here: that is the policy's to say.
        RuleFor(model => model.NewPassword).NotEmpty().MaximumLength(CredentialCheckValidator.PasswordMaxLength);
    }
}
