namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>
/// What the Media module said about an exhibition's hero image, asked by the facade (media.md: "before publishing
/// content with an image, a content module calls <c>IMediaFacade.CheckAttachableAsync</c>").
/// </summary>
/// <param name="HeroMediaId">The image the facade asked about, so Business can tell it is still the exhibition's.</param>
/// <param name="Exists">Whether it is still an original in the library.</param>
/// <param name="HasAltText">Whether it has the alt text published content requires.</param>
internal sealed record ExhibitionHeroCheck(Guid? HeroMediaId, bool Exists, bool HasAltText)
{
    /// <summary>The answer for an exhibition with no hero: nothing to check.</summary>
    public static ExhibitionHeroCheck None { get; } = new(null, Exists: true, HasAltText: true);
}
