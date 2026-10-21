using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Instructors.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Instructors.Managers.Validation;

/// <summary>Shape only. Whether the slug is free, or the portrait exists, is answered by Business and the facade.</summary>
internal sealed class InstructorCreateValidator : AbstractValidator<InstructorCreateViewModel>
{
    public InstructorCreateValidator()
    {
        RuleFor(create => create.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter the instructor's name.")
            .MaximumLength(InstructorPolicy.NameMaxLength);

        // With no slug sent, one is made from the name, which needs at least one letter or digit to make.
        RuleFor(create => create.Name)
            .Must(name => Slugs.FromName(name).Length > 0)
            .When(create => create.Slug is null && !string.IsNullOrWhiteSpace(create.Name))
            .WithMessage("The name needs a letter or digit to make a web address from. Enter a slug instead.");

        RuleFor(create => create.Slug!)
            .Must(Slugs.IsValid).When(create => create.Slug is not null)
            .WithMessage(SlugMessage);

        RuleFor(create => create.Medium).MaximumLength(InstructorPolicy.MediumMaxLength);
        RuleFor(create => create.Bio).MaximumLength(InstructorPolicy.BioMaxLength);
        RuleFor(create => create.PortraitMediaId).NotEqual(Guid.Empty).When(create => create.PortraitMediaId is not null);
        RuleFor(create => create.ArtistId).NotEqual(Guid.Empty).When(create => create.ArtistId is not null);

        RuleFor(create => create.Website!)
            .Must(InstructorPolicy.IsWebsite).When(create => !string.IsNullOrWhiteSpace(create.Website))
            .WithMessage(WebsiteMessage);

        RuleFor(create => create.Instagram!)
            .Must(handle => InstructorPolicy.IsInstagramHandle(InstructorPolicy.NormalizeInstagram(handle)))
            .When(create => !string.IsNullOrWhiteSpace(create.Instagram))
            .WithMessage(InstagramMessage);
    }

    internal const string SlugMessage =
        "Use lower-case letters, digits and single hyphens, up to 120 characters (for example jane-doe).";

    internal const string WebsiteMessage =
        "Enter the full address, starting https:// (for example https://janedoe.studio), up to 300 characters.";

    internal const string InstagramMessage =
        "Enter the Instagram handle: letters, digits, periods and underscores, up to 30 characters.";
}

internal sealed class InstructorUpdateValidator : AbstractValidator<InstructorUpdateViewModel>
{
    public InstructorUpdateValidator()
    {
        RuleFor(update => update.Name!)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The instructor's name can't be empty.")
            .MaximumLength(InstructorPolicy.NameMaxLength)
            .When(update => update.Name is not null);

        RuleFor(update => update.Slug!)
            .Must(Slugs.IsValid).When(update => update.Slug is not null)
            .WithMessage(InstructorCreateValidator.SlugMessage);

        RuleFor(update => update.Medium).MaximumLength(InstructorPolicy.MediumMaxLength);
        RuleFor(update => update.Bio).MaximumLength(InstructorPolicy.BioMaxLength);
        RuleFor(update => update.PortraitMediaId).NotEqual(Guid.Empty).When(update => update.PortraitMediaId is not null);
        RuleFor(update => update.ArtistId).NotEqual(Guid.Empty).When(update => update.ArtistId is not null);

        // An empty string clears these, so only text with something in it is checked.
        RuleFor(update => update.Website!)
            .Must(InstructorPolicy.IsWebsite).When(update => !string.IsNullOrWhiteSpace(update.Website))
            .WithMessage(InstructorCreateValidator.WebsiteMessage);

        RuleFor(update => update.Instagram!)
            .Must(handle => InstructorPolicy.IsInstagramHandle(InstructorPolicy.NormalizeInstagram(handle)))
            .When(update => !string.IsNullOrWhiteSpace(update.Instagram))
            .WithMessage(InstructorCreateValidator.InstagramMessage);

        RuleFor(update => update.RemoveArtist)
            .Must(remove => remove != true).When(update => update.ArtistId is not null)
            .WithMessage("Send either artistId or removeArtist, not both.");

        RuleFor(update => update.RemovePortrait)
            .Must(remove => remove != true).When(update => update.PortraitMediaId is not null)
            .WithMessage("Send either portraitMediaId or removePortrait, not both.");

        RuleFor(update => update)
            .Must(update => update.Name is not null || update.Slug is not null || update.Medium is not null
                || update.Bio is not null || update.Active is not null || update.PortraitMediaId is not null
                || update.RemovePortrait == true || update.Website is not null || update.Instagram is not null
                || update.ArtistId is not null || update.RemoveArtist == true)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of name, slug, medium, bio, active, portraitMediaId, removePortrait, website, instagram, artistId or removeArtist.");
    }
}

internal sealed class InstructorListValidator : AbstractValidator<InstructorListViewModel>
{
    public InstructorListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Status).IsInEnum();
        RuleFor(list => list.Search).MaximumLength(InstructorPolicy.SearchMaxLength);
    }
}
