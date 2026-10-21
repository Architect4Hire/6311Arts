namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// The content types' names as <c>site.revalidate-requested</c> spells them, written once for the site reads that
/// show one type's records inside another's (ADR 0042): a shop item credits an artist, so its cache entries carry
/// the <c>artist</c> type tag as well as their own.
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="SitePaths"/>: only the literals live in the kernel, because a module may not
/// reach into another's <c>Managers</c> for its audit constant (backend.md "Module rules"). Each must equal its
/// module's <c>ResourceType</c>, which a test holds.
/// </remarks>
public static class SiteContentTypes
{
    public const string Artist = "artist";

    /// <summary>Someone who teaches at the centre (ADR 0043). Class runs name them, so class reads carry this tag too.</summary>
    public const string Instructor = "instructor";

    public const string Exhibition = "exhibition";

    public const string ClassOffering = "class-offering";

    public const string ShopItem = "shop-item";

    public const string MembershipPlan = "membership-plan";

    public const string Page = "page";

    /// <summary>Every content type the site shows, for a read that shows them all (the sitemap).</summary>
    public static IReadOnlyList<string> All { get; } = [Artist, Instructor, Exhibition, ClassOffering, ShopItem, MembershipPlan, Page];
}
