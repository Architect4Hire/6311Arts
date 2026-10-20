using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Pages.Managers.Validation;

/// <summary>
/// Shape only. The body's content — what the sanitizer keeps, its length once kept, its pictures — is Business's,
/// since only the sanitized HTML is what would be stored.
/// </summary>
internal sealed class PageCreateValidator : AbstractValidator<PageCreateViewModel>
{
    public PageCreateValidator()
    {
        RuleFor(create => create.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("Enter the page's title.")
            .MaximumLength(PagePolicy.TitleMaxLength);

        // With no slug sent, one is made from the title, which needs at least one letter or digit to make.
        RuleFor(create => create.Title)
            .Must(title => Slugs.FromName(title).Length > 0)
            .When(create => create.Slug is null && !string.IsNullOrWhiteSpace(create.Title))
            .WithMessage("The title needs a letter or digit to make a web address from. Enter a slug instead.");

        RuleFor(create => create.Slug!)
            .Must(Slugs.IsValid).When(create => create.Slug is not null)
            .WithMessage(SlugMessage);

        RuleFor(create => create.Body).MaximumLength(PagePolicy.BodyInputMaxLength);
    }

    internal const string SlugMessage =
        "Use lower-case letters, digits and single hyphens, up to 120 characters (for example privacy).";
}

internal sealed class PageUpdateValidator : AbstractValidator<PageUpdateViewModel>
{
    public PageUpdateValidator()
    {
        RuleFor(update => update.Title!)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("The page's title can't be empty.")
            .MaximumLength(PagePolicy.TitleMaxLength)
            .When(update => update.Title is not null);

        RuleFor(update => update.Slug!)
            .Must(Slugs.IsValid).When(update => update.Slug is not null)
            .WithMessage(PageCreateValidator.SlugMessage);

        RuleFor(update => update.Body).MaximumLength(PagePolicy.BodyInputMaxLength);

        RuleFor(update => update)
            .Must(update => update.Title is not null || update.Slug is not null || update.Body is not null)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of title, slug or body.");
    }
}

internal sealed class PageListValidator : AbstractValidator<PageListViewModel>
{
    public PageListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Status).IsInEnum();
        RuleFor(list => list.Search).MaximumLength(PagePolicy.SearchMaxLength);
    }
}
