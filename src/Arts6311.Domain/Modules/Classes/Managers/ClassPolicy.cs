using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>Field limits for a class offering. The admin's form uses the same numbers (<c>CLASS_LIMITS</c>).</summary>
public static class ClassPolicy
{
    public const int TitleMaxLength = 200;

    /// <summary>The card text and meta description: a search result shows about this much.</summary>
    public const int SummaryMaxLength = 500;

    /// <summary>
    /// The longest body stored, after sanitizing. Beyond <c>nvarchar</c>'s 4,000, so the column is
    /// <c>nvarchar(max)</c>.
    /// </summary>
    public const int BodyMaxLength = 100_000;

    /// <summary>
    /// The longest body accepted before sanitizing. Generous, since markup the sanitizer removes counts here; within
    /// the API's 1 MiB request limit.
    /// </summary>
    public const int BodyInputMaxLength = 200_000;

    public const int ClassTypeMaxLength = 60;

    public const int LevelMaxLength = 60;

    public const int InstructorMaxLength = 200;

    public const int CadenceMaxLength = 100;

    /// <summary>
    /// The most sittings one offering has. The spike's longest course runs eight weeks; a year of a weekly drop-in
    /// is still within this.
    /// </summary>
    public const int SessionsMaxCount = 60;

    /// <summary>The most seats one session has. The main gallery seats fewer than this with easels.</summary>
    public const int CapacityMaxSeats = 500;

    /// <summary>
    /// The highest starting price staff can set, in cents ($10,000). A guard against a typo creating a Square
    /// variation priced in the millions, not a business rule.
    /// </summary>
    public const long StartingPriceMaxCents = 1_000_000;

    /// <summary>How long a session may run, as a guard against a typo: a week-long intensive is still shorter.</summary>
    public static readonly TimeSpan SessionMaxDuration = TimeSpan.FromDays(7);

    /// <summary>The longest IANA zone id stored: the longest in the database is well under this.</summary>
    public const int DisplayTimeZoneMaxLength = 64;

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>
    /// Where a published offering lives on the site: its address is this plus its slug. A slug change after publish
    /// redirects the old address to the new one (ADR 0035).
    /// </summary>
    public const string SitePathPrefix = SitePaths.Classes;

    /// <summary>Identity's user id column length, so <c>UpdatedBy</c> can hold any user id.</summary>
    internal const int UserIdMaxLength = 450;
}
