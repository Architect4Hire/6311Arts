using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// URL slugs for CMS content: lower-case ASCII letters and digits in hyphen-separated runs. Unique per type, and made
/// from the name when a create names none (content.md); those rules are each module's Business. This is the shape,
/// and the numbering a made one takes when its first choice is taken.
/// </summary>
public static partial class Slugs
{
    public const int MaxLength = 120;

    /// <summary>
    /// The slug a name gives, as the spike's <c>slugify</c> makes it: accents dropped, every other run of
    /// non-alphanumerics a single hyphen, none at either end. Empty when the name has no letters or digits.
    /// </summary>
    public static string FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var decomposed = name.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingHyphen = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(character);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = builder.ToString();
        return slug.Length <= MaxLength ? slug : slug[..MaxLength].TrimEnd('-');
    }

    /// <summary>
    /// The slug with <c>-{number}</c> on the end, shortened first so the whole stays within <see cref="MaxLength"/>.
    /// </summary>
    public static string Numbered(string slug, int number)
    {
        ArgumentNullException.ThrowIfNull(slug);

        var suffix = string.Create(CultureInfo.InvariantCulture, $"-{number}");
        var room = MaxLength - suffix.Length;
        return (slug.Length <= room ? slug : slug[..room].TrimEnd('-')) + suffix;
    }

    /// <summary>
    /// The first of <paramref name="slug"/>, <c>slug-2</c>, <c>slug-3</c>… that <paramref name="isTaken"/> says is free:
    /// a web address made from a name never refuses anybody, as two prints both called "Untitled" should both save.
    /// The unique index still has the last word, so a create that races another for the same one is refused.
    /// </summary>
    public static async Task<string> FirstFreeAsync(
        string slug, Func<string, CancellationToken, Task<bool>> isTaken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(isTaken);

        var candidate = slug;
        for (var number = 2; await isTaken(candidate, cancellationToken); number++)
        {
            candidate = Numbered(slug, number);
        }

        return candidate;
    }

    public static bool IsValid(string? slug) =>
        slug is { Length: > 0 and <= MaxLength } && Shape().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
