namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>Field limits for an exhibition. The admin's form uses the same numbers (<c>EXHIBITION_LIMITS</c>).</summary>
public static class ExhibitionPolicy
{
    public const int TitleMaxLength = 200;

    /// <summary>The card text and meta description: a search result shows about this much.</summary>
    public const int SummaryMaxLength = 500;

    /// <summary>Plain text until the sanitizer (2.6). Beyond <c>nvarchar</c>'s 4,000, so the column is <c>nvarchar(max)</c>.</summary>
    public const int BodyMaxLength = 10_000;

    public const int ReceptionNoteMaxLength = 200;

    /// <summary>The most artists one show credits. The spike's largest group show has seven.</summary>
    public const int ArtistsMaxCount = 50;

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>
    /// Where a published exhibition lives on the site: its address is this plus its slug. A slug change after publish
    /// redirects the old address to the new one (ADR 0035).
    /// </summary>
    public const string SitePathPrefix = "/exhibitions/";

    /// <summary>Identity's user id column length, so <c>UpdatedBy</c> can hold any user id.</summary>
    internal const int UserIdMaxLength = 450;
}
