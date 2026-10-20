namespace Arts6311.Domain.Modules.Pages.Managers;

/// <summary>
/// What the Media module said about the library images a body shows, asked by the facade before Business stores the
/// body (media.md: a content module asks <c>IMediaFacade.CheckAttachableAsync</c>; cross-module is facade to facade).
/// </summary>
/// <remarks>
/// Only existence is asked. A body image carries its own <c>alt</c>, which is what the site renders, and Business
/// requires it on every image (ADR 0034). Once stored, a <c>Restrict</c> reference keeps the image from being deleted,
/// so publishing asks Media nothing.
/// </remarks>
/// <param name="Asked">The images the facade asked about, so Business can tell the answer covers the body it is saving.</param>
/// <param name="Missing">Those that are not originals in the library.</param>
internal sealed record PageImageCheck(IReadOnlySet<Guid> Asked, IReadOnlySet<Guid> Missing)
{
    /// <summary>The answer for a body with no images: nothing to check.</summary>
    public static PageImageCheck None { get; } = new(new HashSet<Guid>(), new HashSet<Guid>());
}
