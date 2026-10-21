using System.Text.RegularExpressions;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Instructors.Managers;

/// <summary>Field limits and shapes for an instructor. The admin's form uses the same numbers (<c>INSTRUCTOR_LIMITS</c>).</summary>
public static partial class InstructorPolicy
{
    public const int NameMaxLength = 200;

    public const int MediumMaxLength = 100;

    public const int BioMaxLength = 5000;

    /// <summary>The longest website address kept: far beyond any real one, short enough to index if ever needed.</summary>
    public const int WebsiteMaxLength = 300;

    /// <summary>Instagram's own limit on a handle.</summary>
    public const int InstagramMaxLength = 30;

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>The most instructors another module may look up in one call (<c>IInstructorsFacade.GetSummariesAsync</c>).</summary>
    public const int LookupMaxCount = 200;

    /// <summary>
    /// Where a published instructor lives on the site: its address is this plus its slug. A slug change after publish
    /// redirects the old address to the new one (ADR 0035).
    /// </summary>
    public const string SitePathPrefix = SitePaths.Instructors;

    /// <summary>Identity's user id column length, so <c>UpdatedBy</c> can hold any user id.</summary>
    internal const int UserIdMaxLength = 450;

    /// <summary>
    /// An absolute <c>https:</c> address with a host. Plain <c>http:</c> is refused: the site would link a visitor
    /// off an HTTPS page to an unencrypted one, and every instructor site worth linking serves HTTPS.
    /// </summary>
    public static bool IsWebsite(string value) =>
        value.Length <= WebsiteMaxLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>Instagram's handle shape, without the <c>@</c>: letters, digits, periods and underscores, up to 30.</summary>
    public static bool IsInstagramHandle(string value) => InstagramHandle().IsMatch(value);

    /// <summary>A handle as staff may type it: trimmed, a leading <c>@</c> dropped. Instagram handles are case-insensitive.</summary>
    public static string NormalizeInstagram(string value) => value.Trim().TrimStart('@').ToLowerInvariant();

    [GeneratedRegex("^[a-z0-9._]{1,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstagramHandle();
}
