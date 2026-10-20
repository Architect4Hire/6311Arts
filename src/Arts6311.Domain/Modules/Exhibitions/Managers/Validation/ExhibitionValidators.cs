using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Exhibitions.Managers.Validation;

/// <summary>
/// Shape only. Whether the slug is free, the hero exists or the artists are on the roster is answered by Business
/// and the facade; whether an edit's dates still fit the dates it leaves alone, by Business.
/// </summary>
internal sealed class ExhibitionCreateValidator : AbstractValidator<ExhibitionCreateViewModel>
{
    public ExhibitionCreateValidator()
    {
        RuleFor(create => create.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("Enter the exhibition's title.")
            .MaximumLength(ExhibitionPolicy.TitleMaxLength);

        // With no slug sent, one is made from the title, which needs at least one letter or digit to make.
        RuleFor(create => create.Title)
            .Must(title => Slugs.FromName(title).Length > 0)
            .When(create => create.Slug is null && !string.IsNullOrWhiteSpace(create.Title))
            .WithMessage("The title needs a letter or digit to make a web address from. Enter a slug instead.");

        RuleFor(create => create.Slug!)
            .Must(Slugs.IsValid).When(create => create.Slug is not null)
            .WithMessage(ExhibitionRules.SlugMessage);

        RuleFor(create => create.StartDate).NotNull().WithMessage("Enter the first day of the show.");
        RuleFor(create => create.EndDate).NotNull().WithMessage("Enter the last day of the show.");
        RuleFor(create => create.EndDate)
            .Must((create, end) => end >= create.StartDate)
            .When(create => create.StartDate is not null && create.EndDate is not null)
            .WithMessage(ExhibitionRules.EndBeforeStartMessage);

        RuleFor(create => create.Summary).MaximumLength(ExhibitionPolicy.SummaryMaxLength);
        RuleFor(create => create.Body).MaximumLength(ExhibitionPolicy.BodyMaxLength);
        RuleFor(create => create.HeroMediaId).NotEqual(Guid.Empty).When(create => create.HeroMediaId is not null);

        this.ReceptionRules(create => create.Reception);
        this.ArtistRules(create => create.ArtistIds);
    }
}

internal sealed class ExhibitionUpdateValidator : AbstractValidator<ExhibitionUpdateViewModel>
{
    public ExhibitionUpdateValidator()
    {
        RuleFor(update => update.Title!)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("The exhibition's title can't be empty.")
            .MaximumLength(ExhibitionPolicy.TitleMaxLength)
            .When(update => update.Title is not null);

        RuleFor(update => update.Slug!)
            .Must(Slugs.IsValid).When(update => update.Slug is not null)
            .WithMessage(ExhibitionRules.SlugMessage);

        // Both sent: checked here. One sent: Business checks it against the date already stored.
        RuleFor(update => update.EndDate)
            .Must((update, end) => end >= update.StartDate)
            .When(update => update.StartDate is not null && update.EndDate is not null)
            .WithMessage(ExhibitionRules.EndBeforeStartMessage);

        RuleFor(update => update.Summary).MaximumLength(ExhibitionPolicy.SummaryMaxLength);
        RuleFor(update => update.Body).MaximumLength(ExhibitionPolicy.BodyMaxLength);
        RuleFor(update => update.HeroMediaId).NotEqual(Guid.Empty).When(update => update.HeroMediaId is not null);

        RuleFor(update => update.RemoveHero)
            .Must(remove => remove != true).When(update => update.HeroMediaId is not null)
            .WithMessage("Send either heroMediaId or removeHero, not both.");

        RuleFor(update => update.RemoveReception)
            .Must(remove => remove != true).When(update => update.Reception is not null)
            .WithMessage("Send either reception or removeReception, not both.");

        this.ReceptionRules(update => update.Reception);
        this.ArtistRules(update => update.ArtistIds);

        RuleFor(update => update)
            .Must(update => update.Title is not null || update.Slug is not null || update.Summary is not null
                || update.Body is not null || update.StartDate is not null || update.EndDate is not null
                || update.Reception is not null || update.RemoveReception == true || update.HeroMediaId is not null
                || update.RemoveHero == true || update.ArtistIds is not null)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of title, slug, summary, body, startDate, endDate, "
                + "reception, removeReception, heroMediaId, removeHero or artistIds.");
    }
}

internal sealed class ExhibitionListValidator : AbstractValidator<ExhibitionListViewModel>
{
    public ExhibitionListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Status).IsInEnum();
        RuleFor(list => list.Search).MaximumLength(ExhibitionPolicy.SearchMaxLength);
    }
}

/// <summary>The rules create and edit share, keyed by the camelCase field names the admin's form shows them under.</summary>
internal static class ExhibitionRules
{
    internal const string SlugMessage =
        "Use lower-case letters, digits and single hyphens, up to 120 characters (for example boats-and-pianos).";

    internal const string EndBeforeStartMessage = "The show can't end before it starts.";

    /// <summary>A reception has both times, the end after the start, and a short note if any.</summary>
    public static void ReceptionRules<T>(
        this AbstractValidator<T> validator, Func<T, ExhibitionReceptionViewModel?> reception)
    {
        // Null-safe accessors: a rule's value may be read before its When is, and most requests have no reception.
        validator.RuleFor(model => reception(model) == null ? null : reception(model)!.StartsAt)
            .NotNull().WithMessage("Enter when the reception starts.")
            .OverridePropertyName("reception.startsAt")
            .When(model => reception(model) is not null);

        validator.RuleFor(model => reception(model) == null ? null : reception(model)!.EndsAt)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Enter when the reception ends.")
            .Must((model, end) => reception(model)!.StartsAt is not { } start || end > start)
            .WithMessage("The reception must end after it starts.")
            .OverridePropertyName("reception.endsAt")
            .When(model => reception(model) is not null);

        validator.RuleFor(model => reception(model) == null ? null : reception(model)!.Note)
            .MaximumLength(ExhibitionPolicy.ReceptionNoteMaxLength)
            .OverridePropertyName("reception.note")
            .When(model => reception(model) is not null);
    }

    /// <summary>At most <see cref="ExhibitionPolicy.ArtistsMaxCount"/> credits, each a real id, each once.</summary>
    public static void ArtistRules<T>(this AbstractValidator<T> validator, Func<T, IReadOnlyList<Guid>?> artistIds)
    {
        validator.RuleFor(model => artistIds(model) ?? Array.Empty<Guid>())
            .Must(ids => ids.Count <= ExhibitionPolicy.ArtistsMaxCount)
            .WithMessage($"An exhibition credits at most {ExhibitionPolicy.ArtistsMaxCount} artists.")
            .Must(ids => !ids.Contains(Guid.Empty))
            .WithMessage("Every artist id must be a real id.")
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("Each artist is credited once.")
            .OverridePropertyName("artistIds")
            .When(model => artistIds(model) is not null);
    }
}
