using System.Globalization;
using System.Text.RegularExpressions;

namespace Arts6311.Domain.Managers.Catalog;

/// <summary>
/// What a SKU may look like, and the SKU the CMS suggests for a new record (ADR 0032, 0037). The admin's forms use
/// the same numbers (<c>SKU_LIMITS</c>).
/// </summary>
public static partial class SkuPolicy
{
    /// <summary>
    /// The longest SKU stored. Square's own limit on a variation SKU is larger; this is what fits a label, a
    /// barcode and the admin's grid.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>The prefix for a class run's suggestion: <c>CLS-4-2</c> is the second run of offering 4.</summary>
    public const string ClassPrefix = "CLS";

    /// <summary>The prefix for artwork's suggestion (2.10): <c>ART-17</c>.</summary>
    public const string ArtworkPrefix = "ART";

    /// <summary>
    /// A SKU as it is stored and compared: trimmed and upper-cased, so staff typing <c>cls-4-1</c> and
    /// <c>CLS-4-1</c> mean one SKU rather than two. <c>null</c> or blank in, <c>null</c> out.
    /// </summary>
    public static string? Normalize(string? sku) =>
        string.IsNullOrWhiteSpace(sku) ? null : sku.Trim().ToUpperInvariant();

    /// <summary>
    /// Upper-case letters, digits, hyphens and dots, in runs separated by single hyphens or dots. Narrow on
    /// purpose: a SKU is read aloud, typed at the front desk and printed on a label.
    /// </summary>
    public static bool IsValid(string? sku) =>
        sku is { Length: > 0 and <= MaxLength } && Shape().IsMatch(sku);

    /// <summary>
    /// The suggestion for run <paramref name="runNumber"/> (from 1) of offering <paramref name="offeringNumber"/>:
    /// <c>CLS-&lt;offering&gt;-&lt;run&gt;</c>.
    /// </summary>
    public static string ForClassRun(int offeringNumber, int runNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offeringNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(runNumber);
        return string.Create(CultureInfo.InvariantCulture, $"{ClassPrefix}-{offeringNumber}-{runNumber}");
    }

    /// <summary>The suggestion for artwork number <paramref name="itemNumber"/>: <c>ART-&lt;item&gt;</c>.</summary>
    public static string ForShopItem(int itemNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemNumber);
        return string.Create(CultureInfo.InvariantCulture, $"{ArtworkPrefix}-{itemNumber}");
    }

    [GeneratedRegex("^[A-Z0-9]+(?:[-.][A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
