using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Redirects.Managers.Validation;

/// <summary>
/// Shape: each address on its own is one a redirect may have (<see cref="RedirectPaths"/>), and the code is 301 or
/// 302. Whether it collides with, loops through or chains onto other redirects is Business's, against the table.
/// </summary>
internal sealed class RedirectCreateValidator : AbstractValidator<RedirectCreateViewModel>
{
    public RedirectCreateValidator()
    {
        RuleFor(create => create.FromPath).Custom((from, context) =>
        {
            if (RedirectPaths.NormalizeFrom(from).Error is { } error)
            {
                context.AddFailure("fromPath", error);
            }
        });

        RuleFor(create => create.ToPath).Custom((to, context) =>
        {
            if (RedirectPaths.NormalizeTo(to).Error is { } error)
            {
                context.AddFailure("toPath", error);
            }
        });

        RuleFor(create => create.StatusCode)
            .Must(code => RedirectPolicy.StatusCodes.Contains(code!.Value)).When(create => create.StatusCode is not null)
            .WithMessage("Use 301 (moved for good) or 302 (for now).");
    }
}

internal sealed class RedirectUpdateValidator : AbstractValidator<RedirectUpdateViewModel>
{
    public RedirectUpdateValidator()
    {
        RuleFor(update => update.FromPath).Custom((from, context) =>
        {
            if (from is not null && RedirectPaths.NormalizeFrom(from).Error is { } error)
            {
                context.AddFailure("fromPath", error);
            }
        });

        RuleFor(update => update.ToPath).Custom((to, context) =>
        {
            if (to is not null && RedirectPaths.NormalizeTo(to).Error is { } error)
            {
                context.AddFailure("toPath", error);
            }
        });

        RuleFor(update => update.StatusCode)
            .Must(code => RedirectPolicy.StatusCodes.Contains(code!.Value)).When(update => update.StatusCode is not null)
            .WithMessage("Use 301 (moved for good) or 302 (for now).");

        RuleFor(update => update)
            .Must(update => update.FromPath is not null || update.ToPath is not null || update.StatusCode is not null)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of fromPath, toPath or statusCode.");
    }
}

internal sealed class RedirectListValidator : AbstractValidator<RedirectListViewModel>
{
    public RedirectListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Source).IsInEnum();
        RuleFor(list => list.Search).MaximumLength(RedirectPolicy.SearchMaxLength);
        RuleFor(list => list.Sort).SortableBy(RedirectListOrder.Columns);
    }
}

internal sealed class RedirectSlugChangeValidator : AbstractValidator<RedirectSlugChangeViewModel>
{
    public RedirectSlugChangeValidator()
    {
        RuleFor(change => change.ContentType).NotEmpty().MaximumLength(50);
        RuleFor(change => change.ContentId).NotEqual(Guid.Empty);
        RuleFor(change => change.OccurredAt).NotEqual(default(DateTimeOffset));
        RuleFor(change => change.FromPath).Must(from => RedirectPaths.NormalizeFrom(from).Error is null)
            .WithMessage("The old address isn't one a redirect can leave from.");
        RuleFor(change => change.ToPath).Must(to => RedirectPaths.NormalizeTo(to).Error is null)
            .WithMessage("The new address isn't one a redirect can lead to.");
    }
}
