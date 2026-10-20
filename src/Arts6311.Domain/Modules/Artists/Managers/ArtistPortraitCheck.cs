namespace Arts6311.Domain.Modules.Artists.Managers;

/// <summary>
/// What the Media module said about an artist's portrait, asked by the facade just before a publish (media.md:
/// "before publishing content with an image, a content module calls <c>IMediaFacade.CheckAttachableAsync</c>").
/// </summary>
/// <param name="PortraitMediaId">The portrait the facade asked about, so Business can tell it is still the artist's.</param>
/// <param name="Exists">Whether it is still an original in the library.</param>
/// <param name="HasAltText">Whether it has the alt text published content requires.</param>
internal sealed record ArtistPortraitCheck(Guid? PortraitMediaId, bool Exists, bool HasAltText)
{
    /// <summary>The answer for an artist with no portrait: nothing to check.</summary>
    public static ArtistPortraitCheck None { get; } = new(null, Exists: true, HasAltText: true);
}
