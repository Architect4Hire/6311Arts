using System.Globalization;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Memberships.Managers.Validation;

/// <summary>
/// Shape only. Whether the slug is free, and whether the starting price and cadence may still be set at all, is
/// answered by Business.
/// </summary>
internal sealed class MembershipPlanCreateValidator : AbstractValidator<MembershipPlanCreateViewModel>
{
    public MembershipPlanCreateValidator()
    {
        RuleFor(create => create.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Enter the tier's name.")
            .MaximumLength(MembershipPolicy.NameMaxLength);

        // With no slug sent, one is made from the name, which needs at least one letter or digit to make.
        RuleFor(create => create.Name)
            .Must(name => Slugs.FromName(name).Length > 0)
            .When(create => create.Slug is null && !string.IsNullOrWhiteSpace(create.Name))
            .WithMessage("The name needs a letter or digit to make a key from. Enter a slug instead.");

        RuleFor(create => create.Slug!)
            .Must(Slugs.IsValid).When(create => create.Slug is not null)
            .WithMessage(MembershipPlanRules.SlugMessage);

        RuleFor(create => create.StartingCadence).IsInEnum().When(create => create.StartingCadence is not null);

        this.PerkRules(create => create.Perks);
        this.DiscountRules(create => create.DiscountPercent);
        this.DisplayOrderRules(create => create.DisplayOrder);
        this.PriceRules(create => create.StartingPriceCents);
    }
}

internal sealed class MembershipPlanUpdateValidator : AbstractValidator<MembershipPlanUpdateViewModel>
{
    public MembershipPlanUpdateValidator()
    {
        RuleFor(update => update.Name!)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("The tier's name can't be empty.")
            .MaximumLength(MembershipPolicy.NameMaxLength)
            .When(update => update.Name is not null);

        RuleFor(update => update.Slug!)
            .Must(Slugs.IsValid).When(update => update.Slug is not null)
            .WithMessage(MembershipPlanRules.SlugMessage);

        RuleFor(update => update.StartingCadence).IsInEnum().When(update => update.StartingCadence is not null);

        RuleFor(update => update.RemoveStartingPrice)
            .Must(remove => remove != true).When(update => update.StartingPriceCents is not null)
            .WithMessage("Send either startingPriceCents or removeStartingPrice, not both.");

        this.PerkRules(update => update.Perks);
        this.DiscountRules(update => update.DiscountPercent);
        this.DisplayOrderRules(update => update.DisplayOrder);
        this.PriceRules(update => update.StartingPriceCents);

        RuleFor(update => update)
            .Must(update => update.Name is not null || update.Slug is not null || update.Perks is not null
                || update.DiscountPercent is not null || update.Featured is not null || update.DisplayOrder is not null
                || update.StartingPriceCents is not null || update.RemoveStartingPrice == true
                || update.StartingCadence is not null)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of name, slug, perks, discountPercent, featured, "
                + "displayOrder, startingPriceCents, removeStartingPrice or startingCadence.");
    }
}

internal sealed class MembershipPlanListValidator : AbstractValidator<MembershipPlanListViewModel>
{
    public MembershipPlanListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Status).IsInEnum();
        RuleFor(list => list.Search).MaximumLength(MembershipPolicy.SearchMaxLength);
    }
}

/// <summary>The rules create and edit share, keyed by the camelCase field names the admin's form shows them under.</summary>
internal static class MembershipPlanRules
{
    internal const string SlugMessage =
        "Use lower-case letters, digits and single hyphens, up to 120 characters (for example gray-membership).";

    /// <summary>At most <see cref="MembershipPolicy.PerksMaxCount"/> perks, each with words in it, each once.</summary>
    public static void PerkRules<T>(this AbstractValidator<T> validator, Func<T, IReadOnlyList<string>?> perks)
    {
        validator.RuleFor(model => perks(model) ?? Array.Empty<string>())
            .Must(list => list.Count <= MembershipPolicy.PerksMaxCount)
            .WithMessage($"A tier lists at most {MembershipPolicy.PerksMaxCount} perks.")
            .Must(list => list.All(perk => !string.IsNullOrWhiteSpace(perk)))
            .WithMessage("Every perk needs some words. Remove the empty ones.")
            .Must(list => list.All(perk => perk.Trim().Length <= MembershipPolicy.PerkMaxLength))
            .WithMessage($"Keep each perk under {MembershipPolicy.PerkMaxLength} characters.")
            .Must(list => list.Select(perk => perk.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == list.Count)
            .WithMessage("Each perk is listed once.")
            .OverridePropertyName("perks")
            .When(model => perks(model) is not null);
    }

    /// <summary>A whole-percent class discount, from none to everything.</summary>
    public static void DiscountRules<T>(this AbstractValidator<T> validator, Func<T, int?> discount)
    {
        validator.RuleFor(model => discount(model))
            .InclusiveBetween(0, MembershipPolicy.DiscountMaxPercent)
            .WithMessage($"Enter the class discount as whole percent, from 0 to {MembershipPolicy.DiscountMaxPercent}.")
            .OverridePropertyName("discountPercent")
            .When(model => discount(model) is not null);
    }

    /// <summary>Where the tier comes on the page. From 1, so the first tier is the first one.</summary>
    public static void DisplayOrderRules<T>(this AbstractValidator<T> validator, Func<T, int?> order)
    {
        validator.RuleFor(model => order(model))
            .InclusiveBetween(1, MembershipPolicy.DisplayOrderMax)
            .WithMessage($"Enter where the tier comes, from 1 to {MembershipPolicy.DisplayOrderMax}.")
            .OverridePropertyName("displayOrder")
            .When(model => order(model) is not null);
    }

    /// <summary>
    /// The starting price is cents, never negative, and under the typo guard. Whether it may still be set at all is
    /// Business's: once Square holds the plan, price is Square's (ADR 0005).
    /// </summary>
    public static void PriceRules<T>(this AbstractValidator<T> validator, Func<T, long?> price)
    {
        validator.RuleFor(model => price(model))
            .InclusiveBetween(0, MembershipPolicy.StartingPriceMaxCents)
            .WithMessage(string.Create(
                CultureInfo.InvariantCulture,
                $"Enter the price in cents, from 0 to {MembershipPolicy.StartingPriceMaxCents:N0} (${MembershipPolicy.StartingPriceMaxCents / 100:N0})."))
            .OverridePropertyName("startingPriceCents")
            .When(model => price(model) is not null);
    }
}
