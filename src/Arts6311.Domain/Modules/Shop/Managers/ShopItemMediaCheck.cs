namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// What the Media module said about the images a shop item uses, asked by the facade (media.md: a content module
/// asks <c>IMediaFacade.CheckAttachableAsync</c>; cross-module traffic is facade to facade).
/// </summary>
/// <remarks>
/// Two kinds of image, two questions. A <strong>gallery</strong> photograph is shown on the product page, so it
/// needs alt text before the item is public, as an exhibition's hero does (ADR 0031, 0038). A
/// <strong>description</strong> image carries its own <c>alt</c> inside the HTML, which is what the site renders,
/// so only its existence is asked and Business requires the <c>alt</c> itself (ADR 0034).
/// </remarks>
/// <param name="AskedGallery">The gallery images the facade asked about, so Business can tell the answer covers the gallery it is saving.</param>
/// <param name="MissingGallery">Those that are not originals in the library.</param>
/// <param name="GalleryWithoutAltText">Those with no alt text yet.</param>
/// <param name="AskedDescription">The description's images the facade asked about.</param>
/// <param name="MissingDescription">Those that are not originals in the library.</param>
internal sealed record ShopItemMediaCheck(
    IReadOnlySet<Guid> AskedGallery,
    IReadOnlySet<Guid> MissingGallery,
    IReadOnlySet<Guid> GalleryWithoutAltText,
    IReadOnlySet<Guid> AskedDescription,
    IReadOnlySet<Guid> MissingDescription)
{
    /// <summary>The answer for an item with no images at all: nothing to check.</summary>
    public static ShopItemMediaCheck None { get; } =
        new(new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
}
