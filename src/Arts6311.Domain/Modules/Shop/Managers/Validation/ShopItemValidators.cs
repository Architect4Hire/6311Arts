using System.Globalization;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Shop.Managers.Validation;

/// <summary>
/// Shape only. Whether the slug is free, the SKU is already in use, the artist is on the roster or the photographs
/// are in the library is answered by Business and the facade.
/// </summary>
internal sealed class ShopItemCreateValidator : AbstractValidator<ShopItemCreateViewModel>
{
    public ShopItemCreateValidator()
    {
        RuleFor(create => create.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("Enter the piece's title.")
            .MaximumLength(ShopPolicy.TitleMaxLength);

        // With no slug sent, one is made from the title, which needs at least one letter or digit to make.
        RuleFor(create => create.Title)
            .Must(title => Slugs.FromName(title).Length > 0)
            .When(create => create.Slug is null && !string.IsNullOrWhiteSpace(create.Title))
            .WithMessage("The title needs a letter or digit to make a web address from. Enter a slug instead.");

        RuleFor(create => create.Slug!)
            .Must(Slugs.IsValid).When(create => create.Slug is not null)
            .WithMessage(ShopItemRules.SlugMessage);

        RuleFor(create => create.Description).MaximumLength(ShopPolicy.DescriptionInputMaxLength);
        RuleFor(create => create.ArtistId).NotEqual(Guid.Empty).When(create => create.ArtistId is not null);

        this.SkuRules(create => create.Sku);
        this.CategoryRules(create => create.Categories);
        this.GalleryRules(create => create.ImageMediaIds);
        this.PriceRules(create => create.StartingPriceCents);
        this.StockRules(create => create.StartingStockQuantity);
    }
}

internal sealed class ShopItemUpdateValidator : AbstractValidator<ShopItemUpdateViewModel>
{
    public ShopItemUpdateValidator()
    {
        RuleFor(update => update.Title!)
            .Must(title => !string.IsNullOrWhiteSpace(title)).WithMessage("The piece's title can't be empty.")
            .MaximumLength(ShopPolicy.TitleMaxLength)
            .When(update => update.Title is not null);

        RuleFor(update => update.Slug!)
            .Must(Slugs.IsValid).When(update => update.Slug is not null)
            .WithMessage(ShopItemRules.SlugMessage);

        RuleFor(update => update.Description).MaximumLength(ShopPolicy.DescriptionInputMaxLength);
        RuleFor(update => update.ArtistId).NotEqual(Guid.Empty).When(update => update.ArtistId is not null);

        RuleFor(update => update.RemoveArtist)
            .Must(remove => remove != true).When(update => update.ArtistId is not null)
            .WithMessage("Send either artistId or removeArtist, not both.");

        RuleFor(update => update.RemoveStartingPrice)
            .Must(remove => remove != true).When(update => update.StartingPriceCents is not null)
            .WithMessage("Send either startingPriceCents or removeStartingPrice, not both.");

        this.SkuRules(update => update.Sku);
        this.CategoryRules(update => update.Categories);
        this.GalleryRules(update => update.ImageMediaIds);
        this.PriceRules(update => update.StartingPriceCents);
        this.StockRules(update => update.StartingStockQuantity);

        RuleFor(update => update)
            .Must(update => update.Title is not null || update.Slug is not null || update.Description is not null
                || update.ArtistId is not null || update.RemoveArtist == true || update.Categories is not null
                || update.ImageMediaIds is not null || update.Sku is not null || update.OneOfAKind is not null
                || update.StartingPriceCents is not null || update.RemoveStartingPrice == true
                || update.StartingStockQuantity is not null)
            .WithName("body")
            .WithMessage("Nothing to change: send at least one of title, slug, description, artistId, removeArtist, "
                + "categories, imageMediaIds, sku, oneOfAKind, startingPriceCents, removeStartingPrice or "
                + "startingStockQuantity.");
    }
}

internal sealed class ShopItemListValidator : AbstractValidator<ShopItemListViewModel>
{
    public ShopItemListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Status).IsInEnum();
        RuleFor(list => list.Search).MaximumLength(ShopPolicy.SearchMaxLength);
        RuleFor(list => list.Category!)
            .Must(ShopPolicy.IsCategory).When(list => list.Category is not null)
            .WithMessage(ShopItemRules.CategoryMessage);
    }
}

/// <summary>The rules create and edit share, keyed by the camelCase field names the admin's form shows them under.</summary>
internal static class ShopItemRules
{
    internal const string SlugMessage =
        "Use lower-case letters, digits and single hyphens, up to 120 characters (for example copper-cuff).";

    internal static readonly string CategoryMessage =
        $"Choose from: {string.Join(", ", ShopPolicy.Categories.Select(category => category.Slug))}.";

    /// <summary>A SKU in a shape the registry stores. Whether it is free is Business's to ask (ADR 0037).</summary>
    public static void SkuRules<T>(this AbstractValidator<T> validator, Func<T, string?> sku)
    {
        validator.RuleFor(model => sku(model))
            .Must(value => SkuPolicy.IsValid(SkuPolicy.Normalize(value)))
            .WithMessage($"A SKU is letters, digits, hyphens and dots, up to {SkuPolicy.MaxLength} characters (for example ART-17).")
            .OverridePropertyName("sku")
            .When(model => sku(model) is not null);
    }

    /// <summary>Categories from the allow-list, each once (ADR 0038).</summary>
    public static void CategoryRules<T>(this AbstractValidator<T> validator, Func<T, IReadOnlyList<string>?> categories)
    {
        validator.RuleFor(model => categories(model) ?? Array.Empty<string>())
            .Must(list => list.All(ShopPolicy.IsCategory))
            .WithMessage(CategoryMessage)
            .Must(list => list.Distinct(StringComparer.Ordinal).Count() == list.Count)
            .WithMessage("Each category is listed once.")
            .OverridePropertyName("categories")
            .When(model => categories(model) is not null);
    }

    /// <summary>At most <see cref="ShopPolicy.GalleryMaxCount"/> photographs, each a real id, each once.</summary>
    public static void GalleryRules<T>(this AbstractValidator<T> validator, Func<T, IReadOnlyList<Guid>?> gallery)
    {
        validator.RuleFor(model => gallery(model) ?? Array.Empty<Guid>())
            .Must(list => list.Count <= ShopPolicy.GalleryMaxCount)
            .WithMessage($"A piece has at most {ShopPolicy.GalleryMaxCount} photographs.")
            .Must(list => !list.Contains(Guid.Empty))
            .WithMessage("Every photograph must be a real image id.")
            .Must(list => list.Distinct().Count() == list.Count)
            .WithMessage("Each photograph appears once.")
            .OverridePropertyName("imageMediaIds")
            .When(model => gallery(model) is not null);
    }

    /// <summary>
    /// The starting price is cents, never negative, and under the typo guard. Whether it may still be set at all is
    /// Business's: once Square holds the record, a change is a command (ADR 0032).
    /// </summary>
    public static void PriceRules<T>(this AbstractValidator<T> validator, Func<T, long?> price)
    {
        validator.RuleFor(model => price(model))
            .InclusiveBetween(0, ShopPolicy.StartingPriceMaxCents)
            .WithMessage(string.Create(
                CultureInfo.InvariantCulture,
                $"Enter the price in cents, from 0 to {ShopPolicy.StartingPriceMaxCents:N0} (${ShopPolicy.StartingPriceMaxCents / 100:N0})."))
            .OverridePropertyName("startingPriceCents")
            .When(model => price(model) is not null);
    }

    /// <summary>The starting count is at least one: there is no point stocking none of something.</summary>
    public static void StockRules<T>(this AbstractValidator<T> validator, Func<T, int?> stock)
    {
        validator.RuleFor(model => stock(model))
            .InclusiveBetween(1, ShopPolicy.StartingStockMaxQuantity)
            .WithMessage($"Enter how many there are, from 1 to {ShopPolicy.StartingStockMaxQuantity}.")
            .OverridePropertyName("startingStockQuantity")
            .When(model => stock(model) is not null);
    }
}

internal sealed class SiteShopListValidator : AbstractValidator<SiteShopListViewModel>
{
    public SiteShopListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Category!)
            .Must(ShopPolicy.IsCategory).When(list => list.Category is not null)
            .WithMessage(ShopItemRules.CategoryMessage);
        RuleFor(list => list.Artist!)
            .Must(Slugs.IsValid).When(list => list.Artist is not null)
            .WithMessage(ShopItemRules.SlugMessage);
        RuleFor(list => list.Sort!)
            .Must(sort => ShopPolicy.SiteSorts.Contains(sort, StringComparer.Ordinal)).When(list => list.Sort is not null)
            .WithMessage($"Choose from: {string.Join(", ", ShopPolicy.SiteSorts)}.");
        RuleFor(list => list.Cursor).MaximumLength(ShopPolicy.SiteCursorMaxLength);
    }
}
