using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Preview.Managers;

/// <summary>The numbers and names behind preview links (ADR 0036).</summary>
public static class PreviewPolicy
{
    /// <summary>
    /// How long a link works. Long enough to send to a colleague and read the page, short enough that a link
    /// pasted into a chat does not stay a way into unpublished content.
    /// </summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The Data Protection purpose. Versioned, so a change of payload shape invalidates every live token instead
    /// of being read as the old shape.
    /// </summary>
    public const string ProtectorPurpose = "Arts6311.Preview.v1";

    /// <summary>
    /// The longest token string read at all. A token of ours is a few hundred characters; anything longer is not
    /// one, and is refused before any cryptography runs.
    /// </summary>
    public const int TokenMaxLength = 1024;

    /// <summary>
    /// The content types a preview link can be made for, each with where it lives on the site. A type added here
    /// needs a branch in <c>PreviewFacade</c> too.
    /// </summary>
    /// <remarks>
    /// The map lives in this module because this is the one module that may know every content type (ADR 0036);
    /// the prefixes themselves are the kernel's <see cref="SitePaths"/>, so each is written once.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> PathPrefixes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["artist"] = SitePaths.Artists,
            ["instructor"] = SitePaths.Instructors,
            ["exhibition"] = SitePaths.Exhibitions,
            ["page"] = SitePaths.Pages,
            ["class-offering"] = SitePaths.Classes,
            ["shop-item"] = SitePaths.Shop,

            // A membership plan has no address of its own: every published tier is shown on the one page, so this
            // value is the whole path and nothing is appended to it (ADR 0040).
            ["membership-plan"] = SitePaths.Memberships,
        };

    /// <summary>The content types a preview link can be made for.</summary>
    public static IReadOnlyCollection<string> EntityTypes => (IReadOnlyCollection<string>)PathPrefixes.Keys;

    /// <summary>Where the site serves a preview: the token's own address, which is never in the sitemap.</summary>
    public static string PathFor(string token) => $"/preview/{token}";
}
