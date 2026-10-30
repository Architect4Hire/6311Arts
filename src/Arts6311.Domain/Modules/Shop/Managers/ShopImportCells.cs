using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// How an import reads one cell (ADR 0053): forgiving about what a spreadsheet does to a value — a dollar sign, a
/// thousands comma, "Yes" — and strict about what it means. Each reader answers the value or why it isn't one.
/// </summary>
internal static partial class ShopImportCells
{
    private static readonly string[] Yes = ["true", "yes", "y", "1"];
    private static readonly string[] No = ["false", "no", "n", "0"];

    /// <summary>The <c>one_of_a_kind</c> cell: blank is no; anything that isn't yes or no is <c>null</c>.</summary>
    public static bool? ReadFlag(string cell)
    {
        if (cell.Length == 0 || No.Contains(cell, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return Yes.Contains(cell, StringComparer.OrdinalIgnoreCase) ? true : null;
    }

    /// <summary>The <c>starting_price</c> cell, in dollars, as cents. Blank is no price yet.</summary>
    public static (long? Cents, string? Error) ReadPrice(string cell)
    {
        if (cell.Length == 0)
        {
            return (null, null);
        }

        var digits = cell.StartsWith('$') ? cell[1..].TrimStart() : cell;
        if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dollars)
            || decimal.Round(dollars, 2) != dollars)
        {
            return (null, $"“{Shorten(cell)}” isn't a price. Write it in dollars, like 45 or 45.00.");
        }

        var cents = dollars * 100;
        return cents > ShopPolicy.StartingPriceMaxCents
            ? (null, string.Create(CultureInfo.InvariantCulture, $"The most a starting price can be is ${ShopPolicy.StartingPriceMaxCents / 100:N0}."))
            : ((long)cents, null);
    }

    /// <summary>The <c>starting_stock</c> cell: a whole number of pieces. Blank is no count yet.</summary>
    public static (int? Count, string? Error) ReadStock(string cell)
    {
        if (cell.Length == 0)
        {
            return (null, null);
        }

        return int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count is >= 1 and <= ShopPolicy.StartingStockMaxQuantity
            ? (count, null)
            : (null, $"“{Shorten(cell)}” isn't a count. Write how many there are, from 1 to {ShopPolicy.StartingStockMaxQuantity:N0}.");
    }

    /// <summary>
    /// The <c>categories</c> cell: <c>|</c>-separated, each a category's slug or its label, whatever the case. Each
    /// is kept once, in the order given; <paramref name="unknown"/> holds what names no category.
    /// </summary>
    public static IReadOnlyList<string> ReadCategories(string cell, out IReadOnlyList<string> unknown)
    {
        var slugs = new List<string>();
        var notFound = new List<string>();

        foreach (var part in cell.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var category = ShopPolicy.Categories.FirstOrDefault(candidate =>
                string.Equals(candidate.Slug, part, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Label, part, StringComparison.OrdinalIgnoreCase));

            if (category is null)
            {
                notFound.Add(part);
            }
            else if (!slugs.Contains(category.Slug, StringComparer.Ordinal))
            {
                slugs.Add(category.Slug);
            }
        }

        unknown = notFound;
        return slugs;
    }

    /// <summary>
    /// The <c>description</c> cell, plain text, as the HTML the editor would have made of it: a blank line starts a
    /// new paragraph and a single line break stays one. Every character is encoded, so nothing in the cell is ever
    /// read as markup; the sanitizer still has the last word (content.md).
    /// </summary>
    public static string ToHtml(string text)
    {
        var html = new StringBuilder();
        var normalized = text.ReplaceLineEndings("\n");

        foreach (var paragraph in ParagraphBreak().Split(normalized))
        {
            var lines = paragraph.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
            {
                html.Append("<p>").AppendJoin("<br>", lines.Select(Encode)).Append("</p>");
            }
        }

        return html.ToString();
    }

    /// <summary>
    /// A cell's text to quote back in a message, cut to a readable length: a cell holds whatever the file put there,
    /// and a message should not grow with it.
    /// </summary>
    public static string Shorten(string cell)
    {
        const int Longest = 50;
        return cell.Length <= Longest ? cell : cell[..(Longest - 1)] + "…";
    }

    /// <summary>
    /// The five characters markup gives meaning to, as entities, and nothing else: an accent stays the letter it is
    /// rather than becoming <c>&amp;#233;</c> in the stored description.
    /// </summary>
    private static string Encode(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&#39;", StringComparison.Ordinal);

    [GeneratedRegex(@"\n[ \t]*\n\s*", RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphBreak();
}
