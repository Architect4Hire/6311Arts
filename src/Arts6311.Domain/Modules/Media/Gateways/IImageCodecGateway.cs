using Arts6311.Domain.Modules.Media.Managers;

namespace Arts6311.Domain.Modules.Media.Gateways;

/// <summary>
/// The image library, behind the seam (backend.md "Gateways"): decoding, resizing and encoding. Implemented with
/// SkiaSharp (ADR 0030), which no other file names, so the library can be replaced without touching the module.
/// </summary>
internal interface IImageCodecGateway
{
    /// <summary>
    /// Decodes an image with its EXIF orientation applied to the pixels. Waits for a decode slot first: decoding is
    /// memory-heavy (up to about 200 MB at the pixel limit), so the number in flight per process is bounded.
    /// </summary>
    /// <param name="orientation">The EXIF orientation (1–8) from <c>ImageProbe</c>; never the library's own reading.</param>
    /// <param name="minimumLongSide">
    /// The smallest longest side any encode will ask for. A JPEG may then decode at a reduced scale, which is far
    /// cheaper; <c>null</c> decodes at full size.
    /// </param>
    /// <exception cref="ImageUndecodableException">The pixels do not decode.</exception>
    Task<IDecodedImage> DecodeAsync(Stream source, int orientation, int? minimumLongSide, CancellationToken cancellationToken);
}

/// <summary>A decoded image, holding its decode slot until disposed.</summary>
internal interface IDecodedImage : IDisposable
{
    /// <summary>Width as displayed (orientation applied).</summary>
    int Width { get; }

    int Height { get; }

    /// <summary>Encodes at <see cref="ImageEncodeSpec.Width"/> (never wider than the image), keeping the aspect ratio.</summary>
    /// <returns>The encoded width and height.</returns>
    (int Width, int Height) Encode(ImageEncodeSpec spec, Stream destination);
}

/// <param name="Width">The target width; clamped to the image's own, so nothing is upscaled.</param>
/// <param name="Quality">Lossy quality 1–100; ignored when <paramref name="Lossless"/>.</param>
/// <param name="Lossless">PNG always is; WebP when set.</param>
/// <param name="Chroma444">JPEG only: full-resolution colour, for masters and the high-quality preset.</param>
/// <param name="ConvertToSrgb">Renditions are sRGB for the web; a master keeps its own colour profile.</param>
internal sealed record ImageEncodeSpec(
    MediaImageFormat Format, int Width, int Quality, bool Lossless, bool Chroma444, bool ConvertToSrgb);

/// <summary>The header looked like an image, but the pixels did not decode.</summary>
internal sealed class ImageUndecodableException(string message) : Exception(message);
