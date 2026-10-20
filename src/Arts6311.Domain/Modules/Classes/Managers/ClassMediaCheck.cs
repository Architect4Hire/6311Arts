namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// What the Media module said about the images an offering uses, asked by the facade (media.md: a content module
/// asks <c>IMediaFacade.CheckAttachableAsync</c>; cross-module traffic is facade to facade).
/// </summary>
/// <remarks>
/// Two kinds of image, two questions. The <strong>hero</strong> needs alt text before the offering is public, as an
/// exhibition's does (ADR 0031). A <strong>body</strong> image carries its own <c>alt</c> inside the HTML, which is
/// what the site renders, so only its existence is asked and Business requires the <c>alt</c> itself (ADR 0034).
/// </remarks>
/// <param name="HeroMediaId">The hero the facade asked about, so Business can tell it is still the offering's.</param>
/// <param name="HeroExists">Whether the hero is still an original in the library.</param>
/// <param name="HeroHasAltText">Whether the hero has the alt text published content requires.</param>
/// <param name="AskedBodyImages">The body images the facade asked about, so Business can tell the answer covers the body it is saving.</param>
/// <param name="MissingBodyImages">Those that are not originals in the library.</param>
internal sealed record ClassMediaCheck(
    Guid? HeroMediaId,
    bool HeroExists,
    bool HeroHasAltText,
    IReadOnlySet<Guid> AskedBodyImages,
    IReadOnlySet<Guid> MissingBodyImages)
{
    /// <summary>The answer for an offering with no hero and no body images: nothing to check.</summary>
    public static ClassMediaCheck None { get; } =
        new(null, HeroExists: true, HeroHasAltText: true, new HashSet<Guid>(), new HashSet<Guid>());
}
