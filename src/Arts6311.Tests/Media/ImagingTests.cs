using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.Imaging;
using Arts6311.Tests.Media.Infrastructure;
using SkiaSharp;

namespace Arts6311.Tests.Media;

/// <summary>
/// The managed half of the upload path: identifying a file by its bytes, reading its header, and rewriting its
/// metadata to the allow-list (media.md "EXIF location data stripped; copyright kept", ADR 0030).
/// </summary>
public sealed class ImagingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void TheProbeIdentifiesEachFormatByItsSignature()
    {
        Assert.Equal(MediaImageFormat.Jpeg, Probe(ImageFixtures.Jpeg(40, 30))!.Format);
        Assert.Equal(MediaImageFormat.Png, Probe(ImageFixtures.Png(40, 30))!.Format);
        Assert.Equal(MediaImageFormat.Webp, Probe(ImageFixtures.Webp(40, 30))!.Format);
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("html")]
    [InlineData("gif")]
    [InlineData("empty")]
    public void AnythingElseIsNotAnImage(string kind)
    {
        // An SVG can carry script, HTML is HTML, and a GIF is simply not on the list (media.md).
        var bytes = kind switch
        {
            "svg" => ImageFixtures.Svg(),
            "html" => "<!doctype html><title>x</title>"u8.ToArray(),
            "gif" => "GIF89a\x01\0\x01\0\0\0\0;"u8.ToArray(),
            _ => [],
        };

        Assert.Null(Probe(bytes));
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("webp-lossless")]
    public void TheProbeReadsDimensionsFromTheHeader(string kind)
    {
        var bytes = kind switch
        {
            "jpeg" => ImageFixtures.Jpeg(123, 45),
            "png" => ImageFixtures.Png(123, 45),
            "webp" => ImageFixtures.Webp(123, 45),
            _ => ImageFixtures.Webp(123, 45, lossless: true),
        };

        var probe = Probe(bytes)!;

        Assert.Equal((123, 45), (probe.Width, probe.Height));
        Assert.Equal(kind is "png" or "webp-lossless", probe.IsLossless);
    }

    [Fact]
    public void AnEnormousHeaderIsReadWithoutDecodingAnything()
    {
        // 100 000 × 100 000 would be 40 GB decoded. The probe only reads the header, which is what lets Business
        // refuse it before the image library ever sees it.
        var probe = Probe(ImageFixtures.PngHeaderOnly(100_000, 100_000))!;

        Assert.Equal((100_000, 100_000), (probe.Width, probe.Height));
    }

    [Fact]
    public void AnimationIsDetected()
    {
        Assert.True(Probe(ImageFixtures.AnimatedPng())!.IsAnimated);
        Assert.False(Probe(ImageFixtures.Png(10, 10))!.IsAnimated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheKeptTagsAreReadInEitherByteOrder(bool bigEndian)
    {
        var exif = ExifMetadata.Read(ImageFixtures.ExifWithGps(orientation: 6, bigEndian: bigEndian));

        Assert.Equal(new ExifMetadata(6, ImageFixtures.Artist, ImageFixtures.Copyright), exif);
    }

    [Fact]
    public void AnOrientationThatSwapsTheAxesSwapsTheDisplayedSize()
    {
        var probe = Probe(ImageFixtures.Jpeg(60, 20, exifTiff: ImageFixtures.ExifWithGps(orientation: 6)))!;

        Assert.Equal((60, 20), (probe.Width, probe.Height));
        Assert.Equal((20, 60), (probe.DisplayWidth, probe.DisplayHeight));
    }

    [Fact]
    public void MalformedExifIsIgnoredRatherThanRefused()
    {
        // Metadata is never a reason to refuse a picture.
        Assert.Equal(ExifMetadata.None, ExifMetadata.Read("II*\0\xFF\xFF\xFF\x7F"u8));
        Assert.Equal(ExifMetadata.None, ExifMetadata.Read([]));
    }

    [Fact]
    public void TheWrittenTiffReadsBackAsTheSameTags()
    {
        var metadata = new ExifMetadata(8, "A", ImageFixtures.Copyright);

        Assert.Equal(metadata, ExifMetadata.Read(metadata.ToTiff()));
        Assert.Null(ExifMetadata.None.ToTiff());
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public void GpsAndEveryOtherUnlistedTagAreStrippedAndCopyrightIsKept(string kind)
    {
        var exif = ImageFixtures.ExifWithGps(orientation: 3);
        var (bytes, format) = kind switch
        {
            "jpeg" => (ImageFixtures.Jpeg(64, 48, exifTiff: exif, xmp: true), MediaImageFormat.Jpeg),
            "png" => (ImageFixtures.Png(64, 48, exifTiff: exif, xmp: true), MediaImageFormat.Png),
            _ => (ImageFixtures.Webp(64, 48, exifTiff: exif, xmp: true), MediaImageFormat.Webp),
        };
        Assert.True(ImageFixtures.ContainsLeak(bytes), "The fixture should start out leaky.");

        var keep = Probe(bytes)!.Exif;
        var rewritten = Rewrite(bytes, format, keep);

        Assert.False(ImageFixtures.ContainsLeak(rewritten));
        Assert.Equal(new ExifMetadata(3, ImageFixtures.Artist, ImageFixtures.Copyright), Probe(rewritten)!.Exif);

        // Lossless: the pixels are the same pixels.
        Assert.Equal(ImageFixtures.Pixels(bytes), ImageFixtures.Pixels(rewritten));
    }

    [Fact]
    public void ARewrittenPngHasValidChunkChecksumsAndNoTextChunks()
    {
        var bytes = ImageFixtures.Png(32, 32, exifTiff: ImageFixtures.ExifWithGps(), xmp: true);

        var rewritten = Rewrite(bytes, MediaImageFormat.Png, Probe(bytes)!.Exif);

        Assert.True(ImageFixtures.IsValidPngCrc(rewritten));
        var chunks = ImageFixtures.PngChunks(rewritten);
        Assert.DoesNotContain("iTXt", chunks);
        Assert.DoesNotContain("tEXt", chunks);
        Assert.Single(chunks, chunk => chunk == "eXIf");
        Assert.True(chunks.IndexOf("eXIf") < chunks.IndexOf("IDAT"));
    }

    [Fact]
    public void ARewrittenWebpDropsXmpAndKeepsItsFlagsTrue()
    {
        var bytes = ImageFixtures.Webp(32, 32, exifTiff: ImageFixtures.ExifWithGps(), xmp: true);

        var rewritten = Rewrite(bytes, MediaImageFormat.Webp, Probe(bytes)!.Exif);

        var chunks = ImageFixtures.WebpChunks(rewritten);
        Assert.DoesNotContain("XMP ", chunks);
        Assert.Single(chunks, chunk => chunk == "EXIF");

        // VP8X flags: EXIF set, XMP clear.
        Assert.Equal(0x08, rewritten[20] & 0x0C);
        Assert.Equal(rewritten.Length - 8, BitConverter.ToInt32(rewritten, 4));
    }

    [Fact]
    public void ASimpleWebpGainsAnExtendedHeaderWhenItGainsExif()
    {
        // A lossy WebP with no metadata can be the simple format, which has nowhere to put EXIF.
        var simple = SimpleWebp(48, 32);
        Assert.Equal("VP8 ", System.Text.Encoding.ASCII.GetString(simple, 12, 4));

        var rewritten = Rewrite(simple, MediaImageFormat.Webp, new ExifMetadata(1, null, ImageFixtures.Copyright));

        Assert.Equal(["VP8X", "VP8 ", "EXIF"], ImageFixtures.WebpChunks(rewritten));
        Assert.Equal((48, 32), (Probe(rewritten)!.Width, Probe(rewritten)!.Height));
        Assert.Equal(ImageFixtures.Copyright, Probe(rewritten)!.Exif.Copyright);
        Assert.Equal(ImageFixtures.Pixels(simple), ImageFixtures.Pixels(rewritten));
    }

    [Fact]
    public void AReservedJpegMarkerIsNeverCopied()
    {
        // JPG0–JPG13 (F0–FD) code nothing in a baseline or progressive JPEG; a deny-list of APPn would let one carry
        // anything through. The allow-list drops it.
        var jpeg = ImageFixtures.Jpeg(32, 32);
        byte[] reserved = [0xFF, 0xF0, 0x00, (byte)(2 + ImageFixtures.LeakyMake.Length), .. System.Text.Encoding.ASCII.GetBytes(ImageFixtures.LeakyMake)];
        byte[] planted = [.. jpeg[..2], .. reserved, .. jpeg[2..]];

        var rewritten = Rewrite(planted, MediaImageFormat.Jpeg, ExifMetadata.None);

        Assert.False(ImageFixtures.ContainsLeak(rewritten));
        Assert.Equal(ImageFixtures.Pixels(jpeg), ImageFixtures.Pixels(rewritten));
    }

    [Fact]
    public void DataAfterTheJpegEndMarkerIsDropped()
    {
        // Phones append depth maps and gain maps after the main image (MPF), each with its own EXIF.
        var trailing = ImageFixtures.Jpeg(16, 16, exifTiff: ImageFixtures.ExifWithGps());
        var bytes = ImageFixtures.Jpeg(64, 48, trailing: trailing);

        var rewritten = Rewrite(bytes, MediaImageFormat.Jpeg, ExifMetadata.None);

        Assert.False(ImageFixtures.ContainsLeak(rewritten));
        Assert.Equal([0xFF, 0xD9], rewritten[^2..]);
        Assert.True(rewritten.Length < bytes.Length - trailing.Length + 64);
    }

    [Fact]
    public void ABrokenStructureIsReportedAsInvalidData()
    {
        var bytes = ImageFixtures.Png(32, 32);

        Assert.Throws<InvalidDataException>(() => Rewrite(bytes[..40], MediaImageFormat.Png, ExifMetadata.None));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void EveryOrientationPutsTheTopLeftCornerWhereTheEyeExpectsIt(int orientation)
    {
        // The stored image is the displayed one transformed by the EXIF orientation; applying it must undo that.
        // The red block starts top-left in the *displayed* picture, so transform it the camera's way first.
        using var displayed = ImageFixtures.Picture(40, 20);
        using var stored = Inverse(displayed, orientation);

        using var oriented = SkiaImageCodecGateway.Orient(stored, orientation);

        Assert.Equal((40, 20), (oriented.Width, oriented.Height));
        Assert.Equal(new SKColor(255, 0, 0), oriented.GetPixel(0, 0));
        Assert.NotEqual(new SKColor(255, 0, 0), oriented.GetPixel(39, 19));
    }

    [Fact]
    public async Task ALosslessReencodeAtTheSameSizeIsPixelExact()
    {
        // The PNG master guarantee (ADR 0030): re-encoding to compress it must not change a pixel, alpha included.
        var png = ImageFixtures.Png(50, 40, alpha: true);
        using var codec = new SkiaImageCodecGateway();

        using var image = await codec.DecodeAsync(new MemoryStream(png), orientation: 1, minimumLongSide: null, Ct);
        using var output = new MemoryStream();
        image.Encode(new ImageEncodeSpec(MediaImageFormat.Png, 50, 100, Lossless: true, Chroma444: false, ConvertToSrgb: false), output);

        Assert.Equal(ImageFixtures.Pixels(png), ImageFixtures.Pixels(output.ToArray()));
    }

    [Fact]
    public async Task AJpegRenditionOfATransparentImageIsFlattenedOntoWhite()
    {
        var png = ImageFixtures.Png(20, 20, alpha: true);
        using var codec = new SkiaImageCodecGateway();

        using var image = await codec.DecodeAsync(new MemoryStream(png), orientation: 1, minimumLongSide: null, Ct);
        using var output = new MemoryStream();
        var (width, height) = image.Encode(
            new ImageEncodeSpec(MediaImageFormat.Jpeg, 10, 90, Lossless: false, Chroma444: true, ConvertToSrgb: true), output);

        Assert.Equal((10, 10), (width, height));
        using var decoded = ImageFixtures.Decode(output.ToArray());
        Assert.Equal(SKAlphaType.Opaque, decoded.AlphaType);
    }

    [Fact]
    public async Task NothingIsUpscaled()
    {
        using var codec = new SkiaImageCodecGateway();
        using var image = await codec.DecodeAsync(new MemoryStream(ImageFixtures.Jpeg(300, 200)), 1, null, Ct);
        using var output = new MemoryStream();

        var size = image.Encode(new ImageEncodeSpec(MediaImageFormat.Webp, 1600, 80, false, false, true), output);

        Assert.Equal((300, 200), size);
    }

    [Fact]
    public async Task TruncatedPixelsAreUndecodable()
    {
        var jpeg = ImageFixtures.Jpeg(200, 200);
        using var codec = new SkiaImageCodecGateway();

        await Assert.ThrowsAsync<ImageUndecodableException>(() =>
            codec.DecodeAsync(new MemoryStream(jpeg[..(jpeg.Length / 2)]), 1, null, Ct));
    }

    private static ImageProbeResult? Probe(byte[] bytes) => ImageProbe.Probe(new MemoryStream(bytes));

    private static byte[] Rewrite(byte[] bytes, MediaImageFormat format, ExifMetadata keep)
    {
        using var output = new MemoryStream();
        ImageMetadataRewriter.Rewrite(new MemoryStream(bytes), format, keep, output);
        return output.ToArray();
    }

    private static byte[] SimpleWebp(int width, int height)
    {
        // libwebp's simple format: RIFF + one VP8 chunk. Built from Skia's extended output by keeping only the image.
        var extended = ImageFixtures.Webp(width, height);
        var position = 12;
        while (position + 8 <= extended.Length)
        {
            var size = BitConverter.ToInt32(extended, position + 4);
            if (System.Text.Encoding.ASCII.GetString(extended, position, 4) == "VP8 ")
            {
                var chunk = extended[position..(position + 8 + size + (size & 1))];
                byte[] simple = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. chunk];
                BitConverter.GetBytes(simple.Length - 8).CopyTo(simple, 4);
                return simple;
            }

            position += 8 + size + (size & 1);
        }

        throw new InvalidOperationException("No VP8 chunk.");
    }

    /// <summary>What a camera stores for a displayed picture, given the orientation tag it writes.</summary>
    private static SKBitmap Inverse(SKBitmap displayed, int orientation)
    {
        // Each orientation's inverse: 6 and 8 swap; the reflections and 180° are their own inverses.
        var inverse = orientation switch { 6 => 8, 8 => 6, _ => orientation };
        return SkiaImageCodecGateway.Orient(displayed, inverse);
    }
}
