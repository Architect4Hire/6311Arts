using Arts6311.Domain.Modules.Preview.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Preview.Managers.Validation;

/// <summary>Shape only: a type previews exist for, and an id. Whether the record exists is the module's answer.</summary>
internal sealed class PreviewLinkRequestValidator : AbstractValidator<PreviewLinkRequestViewModel>
{
    public PreviewLinkRequestValidator()
    {
        RuleFor(request => request.EntityType)
            .Must(PreviewPolicy.EntityTypes.Contains)
            .WithMessage($"entityType must be one of: {string.Join(", ", PreviewPolicy.EntityTypes.Order(StringComparer.Ordinal))}.");

        RuleFor(request => request.EntityId).NotEqual(Guid.Empty).WithMessage("Choose a record to preview.");
    }
}
