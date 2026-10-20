using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Artists.Managers.Validation;

/// <summary>Shape only. Whether the slug is free, or the portrait exists, is answered by Business and the facade.</summary>
internal sealed class ArtistCreateValidator : AbstractValidator<ArtistCreateViewModel>
{
    public ArtistCreateValidator()
    {
        RuleFor(create => create.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter the artist's name.")
            .MaximumLength(ArtistPolicy.NameMaxLength);

        // With no slug sent, one is made from the name, which needs at least one letter or digit to make.
        RuleFor(create => create.Name)
            .Must(name => Slugs.FromName(name).Length > 0)
            .When(create => create.Slug is null && !string.IsNullOrWhiteSpace(create.Name))
            .WithMessage("The name needs a letter or digit to make a web address from. Enter a slug instead.");

        RuleFor(create => create.Slug!)
            .Must(Slugs.IsValid).When(create => create.Slug is not null)
            .WithMessage(SlugMessage);

        RuleFor(create => create.Medium).MaximumLength(ArtistPolicy.MediumMaxLength);
        RuleFor(create => create.Bio).MaximumLength(ArtistPolicy.BioMaxLength);
        RuleFor(create => create.PortraitMediaId).NotEqual(Guid.Empty).When(create => create.PortraitMediaId is not null);
    }

    internal const string SlugMessage =
        "Use lower-case letters, digits and single hyphens, up to 120 characters (for example jane-doe).";
}

internal sealed class ArtistUpdateValidator : AbstractValidator<ArtistUpdateViewModel>
{
    public ArtistUpdateValidator()
    {
        RuleFor(update => update.Name!)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The artist's name can't be empty.")
            .MaximumLength(ArtistPolicy.NameMaxLength)
            .When(update => update.Name is not null);

        RuleFor(update => update.Slug!)
            .Must(Slugs.IsValid).When(update => update.Slug is not null)
            .WithMessage(ArtistCreateValidator.SlugMessage);

        RuleFor(update => update.Medium).MaximumLength(ArtistPolicy.MediumMaxLength);
        RuleFor(update => update.Bio).MaximumLength(ArtistPolicy.BioMaxLength);
        RuleFor(update => update.PortraitMediaId).NotEqual(Guid.Empty).When(update => update.PortraitMediaId is not null);

        RuleFor(update => update.RemovePortrait)
            .Must(remove => remove != true).When(update => update.PortraitMediaId is not null)
            .WithMessage("Send either portraitMediaId or removePortrait, not both.");

        RuleFor(update => update)
            .Must(update => update.Name is not null || update.Slug is not null || update.Medium is not null
                || update.Bio is not null || update.Active is not null || update.PortraitMediaId is not null
                || update.RemovePortrait == true)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of name, slug, medium, bio, active, portraitMediaId or removePortrait.");
    }
}

internal sealed class ArtistListValidator : AbstractValidator<ArtistListViewModel>
{
    public ArtistListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Status).IsInEnum();
        RuleFor(list => list.Search).MaximumLength(ArtistPolicy.SearchMaxLength);
    }
}
