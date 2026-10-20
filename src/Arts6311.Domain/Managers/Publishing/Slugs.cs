using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// URL slugs for CMS content: lower-case ASCII letters and digits in hyphen-separated runs. Unique per type and
/// immutable once published (content.md); those rules are each module's Business. This is only the shape.
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

    public static bool IsValid(string? slug) =>
        slug is { Length: > 0 and <= MaxLength } && Shape().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
