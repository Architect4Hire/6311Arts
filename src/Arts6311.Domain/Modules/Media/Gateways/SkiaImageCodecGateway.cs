using System.Runtime.InteropServices;
using Arts6311.Domain.Modules.Media.Managers;
using SkiaSharp;

namespace Arts6311.Domain.Modules.Media.Gateways;

/// <summary>
/// <see cref="IImageCodecGateway"/> on SkiaSharp (MIT; ADR 0030). A singleton: it owns the per-process decode slots.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Exactness.</strong> Pixels are decoded unpremultiplied and the EXIF orientation is applied by remapping
/// them, so a lossless encode at the same size and profile — a PNG master, a lossless WebP master — is bit-exact.
/// Anything that resamples, flattens or converts colour works on a premultiplied copy instead, because resampling
/// unpremultiplied pixels lets the colour of fully transparent pixels bleed into edges.
/// </para>
/// <para>
/// <strong>Downscaling</strong> halves with bilinear filtering until within 2× of the target, then finishes with a
/// Mitchell cubic. A single cubic pass over a large ratio skips most source pixels and aliases.
/// </para>
/// </remarks>
internal sealed class SkiaImageCodecGateway : IImageCodecGateway, IDisposable
{
    private readonly SemaphoreSlim slots = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 2));

    public async Task<IDecodedImage> DecodeAsync(
        Stream source, int orientation, int? minimumLongSide, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        await slots.WaitAsync(cancellationToken);
        try
        {
            return new DecodedImage(Decode(source, orientation, minimumLongSide), slots);
        }
        catch
        {
            slots.Release();
            throw;
        }
    }

    public void Dispose() => slots.Dispose();

    private static SKBitmap Decode(Stream source, int orientation, int? minimumLongSide)
    {
        source.Position = 0;

        // Wrapped explicitly so the codec never disposes the caller's stream: uploads are DeleteOnClose temp files
        // that the caller still needs after decoding (to scrub the upload itself, or to read it again).
        using var native = new SKManagedStream(source, disposeManagedStream: false);
        using var codec = SKCodec.Create(native, out var createResult)
            ?? throw new ImageUndecodableException($"The image could not be opened ({createResult}).");

        var info = codec.Info;

        // The limits again, from the decoder's own reading of the header: the probe checked them first, but two parsers
        // can disagree about a crafted file, and this is the one that allocates (ADR 0030).
        if (info.Width <= 0 || info.Height <= 0
            || Math.Max(info.Width, info.Height) > MediaPolicy.MaxSidePixels
            || (long)info.Width * info.Height > MediaPolicy.MaxPixels)
        {
            throw new ImageUndecodableException("The decoder read dimensions outside the limits.");
        }

        var colorSpace = info.ColorSpace ?? SKColorSpace.CreateSrgb();
        var alphaType = info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul;
        var size = ScaledDecodeSize(codec, minimumLongSide);

        var decodeInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, alphaType, colorSpace);
        var bitmap = new SKBitmap(decodeInfo);
        try
        {
            var result = codec.GetPixels(decodeInfo, bitmap.GetPixels());

            // IncompleteInput decodes the top of a truncated file and leaves the rest grey: a broken picture, refused.
            if (result != SKCodecResult.Success)
            {
                throw new ImageUndecodableException($"The image did not decode ({result}).");
            }

            if (orientation is < 2 or > 8)
            {
                return bitmap;
            }

            var oriented = Orient(bitmap, orientation);
            bitmap.Dispose();
            return oriented;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The smallest size the codec can decode to whose longest side still covers <paramref name="minimumLongSide"/>.
    /// Only JPEG scales during decoding (in eighths); other formats report their full size for every scale.
    /// </summary>
    private static SKSizeI ScaledDecodeSize(SKCodec codec, int? minimumLongSide)
    {
        var full = codec.Info.Size;
        if (minimumLongSide is not { } target || target >= Math.Max(full.Width, full.Height))
        {
            return full;
        }

        for (var eighths = 1; eighths < 8; eighths++)
        {
            var candidate = codec.GetScaledDimensions(eighths / 8f);
            if (Math.Max(candidate.Width, candidate.Height) >= target)
            {
                return candidate;
            }
        }

        return full;
    }

    /// <summary>Applies an EXIF orientation by remapping pixels: exact, and independent of the alpha type.</summary>
    internal static SKBitmap Orient(SKBitmap source, int orientation)
    {
        int w = source.Width, h = source.Height;
        var swap = orientation >= 5;
        var info = source.Info.WithSize(swap ? h : w, swap ? w : h);
        var target = new SKBitmap(info);

        var from = MemoryMarshal.Cast<byte, uint>(source.GetPixelSpan());
        var to = MemoryMarshal.Cast<byte, uint>(target.GetPixelSpan());
        int fromStride = source.RowBytes / 4, toStride = target.RowBytes / 4;

        for (var y = 0; y < info.Height; y++)
        {
            for (var x = 0; x < info.Width; x++)
            {
                var (sx, sy) = orientation switch
                {
                    2 => (w - 1 - x, y),
                    3 => (w - 1 - x, h - 1 - y),
                    4 => (x, h - 1 - y),
                    5 => (y, x),
                    6 => (y, h - 1 - x),
                    7 => (w - 1 - y, h - 1 - x),
                    8 => (w - 1 - y, x),
                    _ => (x, y),
                };
                to[(y * toStride) + x] = from[(sy * fromStride) + sx];
            }
        }

        return target;
    }

    private sealed class DecodedImage(SKBitmap bitmap, SemaphoreSlim slots) : IDecodedImage
    {
        private SKBitmap? premultiplied;
        private bool disposed;

        public int Width => bitmap.Width;

        public int Height => bitmap.Height;

        public (int Width, int Height) Encode(ImageEncodeSpec spec, Stream destination)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(destination);
            ObjectDisposedException.ThrowIf(disposed, this);

            var width = Math.Clamp(spec.Width, 1, bitmap.Width);
            var height = Math.Max(1, (int)Math.Round((double)bitmap.Height * width / bitmap.Width));
            var flatten = spec.Format == MediaImageFormat.Jpeg && bitmap.AlphaType != SKAlphaType.Opaque;
            var sourceSpace = bitmap.ColorSpace ?? SKColorSpace.CreateSrgb();
            var convert = spec.ConvertToSrgb && !sourceSpace.IsSrgb;

            if (width == bitmap.Width && !flatten && !convert)
            {
                EncodeTo(bitmap, spec, destination);
                return (width, height);
            }

            premultiplied ??= ToPremultiplied(bitmap);
            using var scaled = Downscale(premultiplied, width, height);
            var space = spec.ConvertToSrgb ? SKColorSpace.CreateSrgb() : sourceSpace;
            using var target = new SKBitmap(new SKImageInfo(
                width, height, SKColorType.Rgba8888, flatten || bitmap.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul, space));

            using (var canvas = new SKCanvas(target))
            {
                // A JPEG has no alpha: transparent areas become white, as they look on the site's pages.
                canvas.Clear(flatten ? SKColors.White : SKColors.Transparent);
                // Drawn 1:1, so nearest sampling copies pixels; the canvas converts the colour space and flattens.
                using var image = SKImage.FromBitmap(scaled ?? premultiplied);
                canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            }

            EncodeTo(target, spec, destination);
            return (width, height);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            premultiplied?.Dispose();
            bitmap.Dispose();
            slots.Release();
        }

        private static SKBitmap ToPremultiplied(SKBitmap source)
        {
            if (source.AlphaType == SKAlphaType.Opaque)
            {
                return source.Copy();
            }

            var target = new SKBitmap(source.Info.WithAlphaType(SKAlphaType.Premul));
            if (!source.PeekPixels().ReadPixels(target.Info, target.GetPixels(), target.RowBytes))
            {
                target.Dispose();
                throw new ImageUndecodableException("The pixels could not be premultiplied.");
            }

            return target;
        }

        /// <returns>A new bitmap, or <c>null</c> when <paramref name="source"/> is already the size.</returns>
        private static SKBitmap? Downscale(SKBitmap source, int width, int height)
        {
            if (source.Width == width && source.Height == height)
            {
                return null;
            }

            var current = source;
            while (current.Width / 2 >= width * 2)
            {
                var half = current.Resize(
                    current.Info.WithSize(Math.Max(1, current.Width / 2), Math.Max(1, current.Height / 2)),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None))
                    ?? throw new ImageUndecodableException("The image could not be resized.");
                if (!ReferenceEquals(current, source))
                {
                    current.Dispose();
                }

                current = half;
            }

            var final = current.Resize(current.Info.WithSize(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
                ?? throw new ImageUndecodableException("The image could not be resized.");
            if (!ReferenceEquals(current, source))
            {
                current.Dispose();
            }

            return final;
        }

        private static void EncodeTo(SKBitmap source, ImageEncodeSpec spec, Stream destination)
        {
            using var pixmap = source.PeekPixels();
            using var data = spec.Format switch
            {
                MediaImageFormat.Jpeg => pixmap.Encode(new SKJpegEncoderOptions(
                    spec.Quality,
                    spec.Chroma444 ? SKJpegEncoderDownsample.Downsample444 : SKJpegEncoderDownsample.Downsample420,
                    SKJpegEncoderAlphaOption.Ignore)),
                MediaImageFormat.Png => pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9)),

                // For lossless WebP the "quality" is effort; 75 is libwebp's own default trade of time for size.
                MediaImageFormat.Webp => pixmap.Encode(spec.Lossless
                    ? new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 75)
                    : new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, spec.Quality)),
                _ => throw new ArgumentOutOfRangeException(nameof(spec)),
            } ?? throw new ImageUndecodableException($"The image could not be encoded as {spec.Format}.");

            data.SaveTo(destination);
        }
    }
}
