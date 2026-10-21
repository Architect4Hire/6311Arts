using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Memberships.Managers;

/// <summary>Field limits for a membership plan. The admin's form uses the same numbers (<c>MEMBERSHIP_LIMITS</c>).</summary>
public static class MembershipPolicy
{
    /// <summary>A tier's name is a word or two: “Gray”, “Black”, “Blue”.</summary>
    public const int NameMaxLength = 100;

    /// <summary>The longest one perk line can be: a sentence on a card, not a paragraph.</summary>
    public const int PerkMaxLength = 200;

    /// <summary>The most perks one tier lists. The page shows them all, so a longer list stops being readable.</summary>
    public const int PerksMaxCount = 12;

    /// <summary>The largest class discount a tier can give, as whole percent.</summary>
    public const int DiscountMaxPercent = 100;

    /// <summary>The highest display order staff can set; the page shows a handful of tiers, not a hundred.</summary>
    public const int DisplayOrderMax = 100;

    /// <summary>
    /// The highest starting price staff can set, in cents ($5,000). A guard against a typo creating a Square plan
    /// that bills in the thousands, not a business rule: the spike's dearest tier is $500 a year.
    /// </summary>
    public const long StartingPriceMaxCents = 500_000;

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>The longest cadence Square reports, so the projection column is sized once.</summary>
    public const int CadenceMaxLength = 32;

    /// <summary>
    /// Where published plans are shown: one page for all of them. A plan has no address of its own, so this is the
    /// whole path rather than a prefix a slug is added to (ADR 0040), and it is what a status change revalidates.
    /// </summary>
    public const string SitePath = SitePaths.Memberships;

    /// <summary>Identity's user id column length, so <c>UpdatedBy</c> can hold any user id.</summary>
    internal const int UserIdMaxLength = 450;
}
