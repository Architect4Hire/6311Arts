namespace Arts6311.Domain.Modules.Artists.Managers;

/// <summary>Field limits for an artist. The admin's form uses the same numbers (<c>ARTIST_LIMITS</c>).</summary>
public static class ArtistPolicy
{
    public const int NameMaxLength = 200;

    public const int MediumMaxLength = 100;

    public const int BioMaxLength = 5000;

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>Identity's user id column length, so <c>UpdatedBy</c> can hold any user id.</summary>
    internal const int UserIdMaxLength = 450;
}
