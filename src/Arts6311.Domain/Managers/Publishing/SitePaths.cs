namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// The site's content path prefixes, written once: a record's address is its type's prefix plus its slug
/// (content.md "Lifecycle"). Each module's policy exposes its own as <c>SitePathPrefix</c> and takes it from here.
/// </summary>
/// <remarks>
/// Only the literals live here, because two places outside a module need them and may not reach past another
/// module's facade (backend.md "Module rules"): the redirect a slug change owes, and the preview link. Which
/// content type has which prefix is <strong>not</strong> here — that map belongs to the one module that knows
/// every type, <c>Modules/Preview</c> (ADR 0036).
/// </remarks>
public static class SitePaths
{
    public const string Artists = "/artists/";

    public const string Exhibitions = "/exhibitions/";

    /// <summary>A class offering: <c>/classes/intro-to-screen-printing</c>.</summary>
    public const string Classes = "/classes/";

    /// <summary>One piece of artwork in the shop: <c>/shop/australian-opal-silver-ring</c>.</summary>
    public const string Shop = "/shop/";

    /// <summary>
    /// Where every published membership plan is shown: one page for all of them, so this is a whole path and not a
    /// prefix a slug is added to — a plan has no address of its own (ADR 0040).
    /// </summary>
    public const string Memberships = "/memberships";

    /// <summary>A page lives at the root: <c>/privacy</c>, <c>/terms</c>.</summary>
    public const string Pages = "/";

    /// <summary>A record's address: its type's prefix plus its slug.</summary>
    public static string AddressOf(string pathPrefix, string slug)
    {
        ArgumentException.ThrowIfNullOrEmpty(pathPrefix);
        ArgumentException.ThrowIfNullOrEmpty(slug);
        return pathPrefix + slug;
    }
}
