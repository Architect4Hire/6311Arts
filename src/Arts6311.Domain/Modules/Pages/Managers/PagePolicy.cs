using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Pages.Managers;

/// <summary>Field limits for a page. The admin's form uses the same numbers (<c>PAGE_LIMITS</c>).</summary>
public static class PagePolicy
{
    public const int TitleMaxLength = 200;

    /// <summary>
    /// The longest body stored, after sanitizing: a long policy is about 60,000 characters of HTML. Beyond
    /// <c>nvarchar</c>'s 4,000, so the column is <c>nvarchar(max)</c>.
    /// </summary>
    public const int BodyMaxLength = 100_000;

    /// <summary>
    /// The longest body accepted before sanitizing. Generous, since markup the sanitizer removes counts here; within
    /// the API's 1 MiB request limit.
    /// </summary>
    public const int BodyInputMaxLength = 200_000;

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>
    /// Where a published page lives on the site: its address is this plus its slug. A slug change after publish
    /// redirects the old address to the new one (ADR 0035).
    /// </summary>
    public const string SitePathPrefix = SitePaths.Pages;

    /// <summary>
    /// Slugs a page can't have: a page lives at <c>/{slug}</c>, so these would sit on an address the gateway, staff
    /// sign-in or another content type already answers — and a rename away from one couldn't be redirected
    /// (<c>RedirectPaths.Reserved</c>, ADR 0035).
    /// </summary>
    public static IReadOnlySet<string> ReservedSlugs { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "api", "bff", "media", "webhooks", "health", "alive", "signin", "account",
        "artists", "artist", "exhibitions", "classes", "instructors", "shop", "product", "memberships",
    };

    /// <summary>
    /// <c>about</c>, <c>visit</c> and <c>submit</c> are deliberately **not** reserved, although the site serves
    /// each as a static route today (<c>projects/site</c> <c>app.routes.ts</c>), so a page created at one of them
    /// would be shadowed by the component the router matches first.
    /// </summary>
    /// <remarks>
    /// Reserving them is the wrong way round. Those three are the spike's hard-coded copy, and <c>/privacy</c> and
    /// <c>/terms</c> next to them already render CMS pages — so the direction of travel is that these become CMS
    /// pages too, not that the CMS is forbidden from the most obvious page a gallery writes. "About" is the
    /// canonical example page throughout the Pages tests, which is the codebase saying the same thing.
    /// <para>
    /// So it is 3.3's call (the prompt that moves the site's routing onto the platform): either those routes start
    /// reading the CMS like <c>/privacy</c> does, or the site keeps them and they join the set above. Until then
    /// staff can publish a page nobody can reach, which is a real if small trap — recorded here rather than
    /// closed off, because closing it off is the harder decision to undo.
    /// </para>
    /// </remarks>

    /// <summary>Identity's user id column length, so <c>UpdatedBy</c> can hold any user id.</summary>
    internal const int UserIdMaxLength = 450;
}
