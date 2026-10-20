using System.Globalization;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Classes.Managers.Validation;

/// <summary>
/// Shape only. Whether the slug is free, a SKU is already in use, the hero exists or the capacity is above the seats
/// already taken is answered by Business and the facade.
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
        RuleFor(create => create.Instructor).MaximumLength(ClassPolicy.InstructorMaxLength);
        RuleFor(create => create.Cadence).MaximumLength(ClassPolicy.CadenceMaxLength);
        RuleFor(create => create.HeroMediaId).NotEqual(Guid.Empty).When(create => create.HeroMediaId is not null);

        this.StartingPriceRules(create => create.StartingPriceCents);
        this.SessionRules(create => create.Sessions);
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
        RuleFor(update => update.Instructor).MaximumLength(ClassPolicy.InstructorMaxLength);
        RuleFor(update => update.Cadence).MaximumLength(ClassPolicy.CadenceMaxLength);
        RuleFor(update => update.HeroMediaId).NotEqual(Guid.Empty).When(update => update.HeroMediaId is not null);

        RuleFor(update => update.RemoveHero)
            .Must(remove => remove != true).When(update => update.HeroMediaId is not null)
            .WithMessage("Send either heroMediaId or removeHero, not both.");

        RuleFor(update => update.RemoveStartingPrice)
            .Must(remove => remove != true).When(update => update.StartingPriceCents is not null)
            .WithMessage("Send either startingPriceCents or removeStartingPrice, not both.");

        this.StartingPriceRules(update => update.StartingPriceCents);
        this.SessionRules(update => update.Sessions);

        RuleFor(update => update)
            .Must(update => update.Title is not null || update.Slug is not null || update.Summary is not null
                || update.Body is not null || update.ClassType is not null || update.Level is not null
                || update.Instructor is not null || update.Cadence is not null || update.StartingPriceCents is not null
                || update.RemoveStartingPrice == true || update.HeroMediaId is not null || update.RemoveHero == true
                || update.Sessions is not null)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of title, slug, summary, body, classType, level, "
                + "instructor, cadence, startingPriceCents, removeStartingPrice, heroMediaId, removeHero or sessions.");
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
    /// The starting price is cents, never negative, and under the typo guard. Whether it may still be set at all is
    /// Business's: once Square holds the record, a change is a command (ADR 0032).
    /// </summary>
    public static void StartingPriceRules<T>(this AbstractValidator<T> validator, Func<T, long?> price)
    {
        validator.RuleFor(model => price(model))
            .InclusiveBetween(0, ClassPolicy.StartingPriceMaxCents)
            .WithMessage(string.Create(
                CultureInfo.InvariantCulture,
                $"Enter the price in cents, from 0 to {ClassPolicy.StartingPriceMaxCents:N0} (${ClassPolicy.StartingPriceMaxCents / 100:N0})."))
            .OverridePropertyName("startingPriceCents")
            .When(model => price(model) is not null);
    }

    /// <summary>
    /// The sessions as a set: at most <see cref="ClassPolicy.SessionsMaxCount"/>, each with both times and a
    /// capacity, and any SKU or display zone sent in a shape we store. Every message is keyed to <c>sessions</c>,
    /// which is the field the admin's grid shows them under.
    /// </summary>
    public static void SessionRules<T>(
        this AbstractValidator<T> validator, Func<T, IReadOnlyList<ClassSessionViewModel>?> sessions)
    {
        // Null-safe accessors: a rule's value may be read before its When is.
        Func<T, IReadOnlyList<ClassSessionViewModel>> rows = model => sessions(model) ?? [];

        validator.RuleFor(model => rows(model))
            .Must(list => list.Count <= ClassPolicy.SessionsMaxCount)
            .WithMessage($"A class has at most {ClassPolicy.SessionsMaxCount} sessions.")

            .Must(list => list.All(session => session.StartsAt is not null && session.EndsAt is not null))
            .WithMessage("Every session needs a start and an end time.")

            .Must(list => list.All(session =>
                session.StartsAt is not { } start || session.EndsAt is not { } end || end > start))
            .WithMessage("A session can't end before it starts.")

            .Must(list => list.All(session =>
                session.StartsAt is not { } start || session.EndsAt is not { } end
                || end - start <= ClassPolicy.SessionMaxDuration))
            .WithMessage($"A session runs at most {ClassPolicy.SessionMaxDuration.TotalDays:N0} days. Add one session per sitting.")

            .Must(list => list.All(session => session.Capacity is not null))
            .WithMessage("Every session needs a number of seats.")

            .Must(list => list.All(session =>
                session.Capacity is not { } capacity || capacity is >= 1 and <= ClassPolicy.CapacityMaxSeats))
            .WithMessage($"Seats run from 1 to {ClassPolicy.CapacityMaxSeats}.")

            .Must(list => list.All(session => session.Sku is null || SkuPolicy.IsValid(SkuPolicy.Normalize(session.Sku))))
            .WithMessage($"A SKU is letters, digits, hyphens and dots, up to {SkuPolicy.MaxLength} characters (for example CLS-4-1).")

            .Must(list => list.All(session =>
                session.DisplayTimeZone is null
                || TimeZoneInfo.TryFindSystemTimeZoneById(session.DisplayTimeZone, out _)))
            .WithMessage("A session's display time zone must be an IANA zone id, for example America/Chicago.")

            .Must(list =>
            {
                var ids = list.Where(session => session.Id is not null).Select(session => session.Id!.Value).ToList();
                return ids.Distinct().Count() == ids.Count;
            })
            .WithMessage("Each session appears once in the list.")

            .Must(list =>
            {
                var skus = list.Select(session => SkuPolicy.Normalize(session.Sku)).Where(sku => sku is not null).ToList();
                return skus.Distinct(StringComparer.Ordinal).Count() == skus.Count;
            })
            .WithMessage("Each session is sold under its own SKU.")

            .OverridePropertyName("sessions")
            .When(model => sessions(model) is not null);
    }
}
