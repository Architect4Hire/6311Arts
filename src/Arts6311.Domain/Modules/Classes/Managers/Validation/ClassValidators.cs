using System.Globalization;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Classes.Managers.Validation;

/// <summary>
/// Shape only. Whether the slug is free, a SKU is already in use, the hero or an instructor exists, or a capacity is
/// above the seats already taken is answered by Business and the facade.
/// </summary>
internal sealed class ClassCreateValidator : AbstractValidator<ClassCreateViewModel>
{
    public ClassCreateValidator()
    {
        RuleFor(create => create.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("Enter the class's title.")
            .MaximumLength(ClassPolicy.TitleMaxLength);

        // With no slug sent, one is made from the title, which needs at least one letter or digit to make.
        RuleFor(create => create.Title)
            .Must(title => Slugs.FromName(title).Length > 0)
            .When(create => create.Slug is null && !string.IsNullOrWhiteSpace(create.Title))
            .WithMessage("The title needs a letter or digit to make a web address from. Enter a slug instead.");

        RuleFor(create => create.Slug!)
            .Must(Slugs.IsValid).When(create => create.Slug is not null)
            .WithMessage(ClassRules.SlugMessage);

        RuleFor(create => create.Summary).MaximumLength(ClassPolicy.SummaryMaxLength);
        RuleFor(create => create.Body).MaximumLength(ClassPolicy.BodyInputMaxLength);
        RuleFor(create => create.ClassType).MaximumLength(ClassPolicy.ClassTypeMaxLength);
        RuleFor(create => create.Level).MaximumLength(ClassPolicy.LevelMaxLength);
        RuleFor(create => create.HeroMediaId).NotEqual(Guid.Empty).When(create => create.HeroMediaId is not null);

        this.RunRules(create => create.Runs);
    }
}

internal sealed class ClassUpdateValidator : AbstractValidator<ClassUpdateViewModel>
{
    public ClassUpdateValidator()
    {
        RuleFor(update => update.Title!)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("The class's title can't be empty.")
            .MaximumLength(ClassPolicy.TitleMaxLength)
            .When(update => update.Title is not null);

        RuleFor(update => update.Slug!)
            .Must(Slugs.IsValid).When(update => update.Slug is not null)
            .WithMessage(ClassRules.SlugMessage);

        RuleFor(update => update.Summary).MaximumLength(ClassPolicy.SummaryMaxLength);
        RuleFor(update => update.Body).MaximumLength(ClassPolicy.BodyInputMaxLength);
        RuleFor(update => update.ClassType).MaximumLength(ClassPolicy.ClassTypeMaxLength);
        RuleFor(update => update.Level).MaximumLength(ClassPolicy.LevelMaxLength);
        RuleFor(update => update.HeroMediaId).NotEqual(Guid.Empty).When(update => update.HeroMediaId is not null);

        RuleFor(update => update.RemoveHero)
            .Must(remove => remove != true).When(update => update.HeroMediaId is not null)
            .WithMessage("Send either heroMediaId or removeHero, not both.");

        this.RunRules(update => update.Runs);

        RuleFor(update => update)
            .Must(update => update.Title is not null || update.Slug is not null || update.Summary is not null
                || update.Body is not null || update.ClassType is not null || update.Level is not null
                || update.HeroMediaId is not null || update.RemoveHero == true || update.Runs is not null)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of title, slug, summary, body, classType, level, "
                + "heroMediaId, removeHero or runs.");
    }
}

internal sealed class ClassListValidator : AbstractValidator<ClassListViewModel>
{
    public ClassListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Status).IsInEnum();
        RuleFor(list => list.Search).MaximumLength(ClassPolicy.SearchMaxLength);
    }
}

/// <summary>The rules create and edit share, keyed by the camelCase field names the admin's form shows them under.</summary>
internal static class ClassRules
{
    internal const string SlugMessage =
        "Use lower-case letters, digits and single hyphens, up to 120 characters (for example intro-to-screen-printing).";

    /// <summary>
    /// The runs as a set (ADR 0044): at most <see cref="ClassPolicy.RunsMaxCount"/>, each with a capacity, any SKU,
    /// zone, price, label and cadence sent in a shape we store, and sessions that each have both times in order.
    /// Every message is keyed to <c>runs</c>, the field the admin's runs panel shows them under. Whether a starting
    /// price may still be set at all is Business's: once Square holds the run, a change is a command (ADR 0032).
    /// </summary>
    public static void RunRules<T>(this AbstractValidator<T> validator, Func<T, IReadOnlyList<ClassRunViewModel>?> runs)
    {
        // Null-safe accessors: a rule's value may be read before its When is.
        Func<T, IReadOnlyList<ClassRunViewModel>> rows = model => runs(model) ?? [];
        static IEnumerable<ClassSessionViewModel> Sessions(IReadOnlyList<ClassRunViewModel> list) =>
            list.SelectMany(run => run.Sessions ?? []);

        validator.RuleFor(model => rows(model))
            .Must(list => list.Count <= ClassPolicy.RunsMaxCount)
            .WithMessage($"A class has at most {ClassPolicy.RunsMaxCount} runs.")

            .Must(list => list.All(run => run.Capacity is not null))
            .WithMessage("Every run needs a number of seats.")

            .Must(list => list.All(run => run.Capacity is not { } capacity || capacity is >= 1 and <= ClassPolicy.CapacityMaxSeats))
            .WithMessage($"Seats run from 1 to {ClassPolicy.CapacityMaxSeats}.")

            .Must(list => list.All(run => run.StartingPriceCents is not { } price || price is >= 0 and <= ClassPolicy.StartingPriceMaxCents))
            .WithMessage(string.Create(
                CultureInfo.InvariantCulture,
                $"Enter a run's price in cents, from 0 to {ClassPolicy.StartingPriceMaxCents:N0} (${ClassPolicy.StartingPriceMaxCents / 100:N0})."))

            .Must(list => list.All(run => run.Label is null || run.Label.Length <= ClassPolicy.RunLabelMaxLength))
            .WithMessage($"A run's label is at most {ClassPolicy.RunLabelMaxLength} characters.")

            .Must(list => list.All(run => run.Cadence is null || run.Cadence.Length <= ClassPolicy.CadenceMaxLength))
            .WithMessage($"A run's cadence is at most {ClassPolicy.CadenceMaxLength} characters.")

            .Must(list => list.All(run => run.InstructorId != Guid.Empty))
            .WithMessage("Choose a run's instructor from the list.")

            .Must(list => list.All(run => run.Sku is null || SkuPolicy.IsValid(SkuPolicy.Normalize(run.Sku))))
            .WithMessage($"A SKU is letters, digits, hyphens and dots, up to {SkuPolicy.MaxLength} characters (for example CLS-4-1).")

            .Must(list => list.All(run =>
                run.DisplayTimeZone is null || TimeZoneInfo.TryFindSystemTimeZoneById(run.DisplayTimeZone, out _)))
            .WithMessage("A run's display time zone must be an IANA zone id, for example America/Chicago.")

            .Must(list => list.All(run => (run.Sessions?.Count ?? 0) <= ClassPolicy.SessionsMaxCount))
            .WithMessage($"A run has at most {ClassPolicy.SessionsMaxCount} sessions.")

            .Must(list => Sessions(list).All(session => session.StartsAt is not null && session.EndsAt is not null))
            .WithMessage("Every session needs a start and an end time.")

            .Must(list => Sessions(list).All(session =>
                session.StartsAt is not { } start || session.EndsAt is not { } end || end > start))
            .WithMessage("A session can't end before it starts.")

            .Must(list => Sessions(list).All(session =>
                session.StartsAt is not { } start || session.EndsAt is not { } end
                || end - start <= ClassPolicy.SessionMaxDuration))
            .WithMessage($"A session runs at most {ClassPolicy.SessionMaxDuration.TotalDays:N0} days. Add one session per sitting.")

            .Must(list => Unique(list.Select(run => run.Id)))
            .WithMessage("Each run appears once in the list.")

            .Must(list => Unique(Sessions(list).Select(session => session.Id)))
            .WithMessage("Each session appears once in the list.")

            .Must(list =>
            {
                var skus = list.Select(run => SkuPolicy.Normalize(run.Sku)).Where(sku => sku is not null).ToList();
                return skus.Distinct(StringComparer.Ordinal).Count() == skus.Count;
            })
            .WithMessage("Each run is sold under its own SKU.")

            .OverridePropertyName("runs")
            .When(model => runs(model) is not null);
    }

    private static bool Unique(IEnumerable<Guid?> ids)
    {
        var sent = ids.OfType<Guid>().ToList();
        return sent.Distinct().Count() == sent.Count;
    }
}
