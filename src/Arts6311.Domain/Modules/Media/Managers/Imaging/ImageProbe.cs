using System.Buffers.Binary;

namespace Arts6311.Domain.Modules.Media.Managers.Imaging;

/// <summary>What the file header says about an upload, read without decoding a single pixel.</summary>
/// <param name="Width">Stored width, before the EXIF orientation is applied. Zero when the header was unreadable.</param>
/// <param name="Height">Stored height, before the EXIF orientation is applied.</param>
public sealed record ImageProbeResult(
    MediaImageFormat Format,
    int Width,
    int Height,
    ExifMetadata Exif,
    bool IsAnimated,
    bool IsLossless,
    bool HasAlpha)
{
    /// <summary>Whether the header carried usable dimensions at all.</summary>
    public bool HasDimensions => Width > 0 && Height > 0;

    /// <summary>Orientations 5–8 swap the axes: the picture as a person sees it.</summary>
    public bool SwapsAxes => Exif.Orientation >= 5;

    public int DisplayWidth => SwapsAxes ? Height : Width;

    public int DisplayHeight => SwapsAxes ? Width : Height;
}

/// <summary>
/// Identifies an upload by its signature — never its extension or the browser's <c>Content-Type</c> (media.md) — and
/// reads its dimensions from the header, so the size limits are enforced before a decompression bomb can expand.
/// </summary>
/// <remarks>
/// Managed code over the container formats only, deliberately independent of the image library: the request path
/// decides what it is holding before handing anything to native code. Anything that is not JPEG, PNG or WebP by
/// its first bytes — an SVG, HTML, a GIF, an executable renamed <c>.jpg</c> — is <c>null</c>.
/// </remarks>
public static class ImageProbe
{
    private const int MaxExifBytes = 1024 * 1024;

    /// <summary>
    /// More segments or chunks than any camera or encoder writes. A file with more is refused rather than walked: a
    /// 25 MB file of empty chunks is millions of iterations of work for nothing.
    /// </summary>
    internal const int MaxStructures = 4096;

    public static ImageProbeResult? Probe(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The probe needs a seekable stream.", nameof(stream));
        }

        stream.Position = 0;
        Span<byte> signature = stackalloc byte[12];
        if (stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false) < signature.Length)
        {
            return null;
        }

        try
        {
            if (signature[0] == 0xFF && signature[1] == 0xD8 && signature[2] == 0xFF)
            {
                return ProbeJpeg(stream);
            }

            if (signature[..8].SequenceEqual(PngSignature))
            {
                return ProbePng(stream);
            }

            if (signature[..4].SequenceEqual("RIFF"u8) && signature[8..12].SequenceEqual("WEBP"u8))
            {
                return ProbeWebp(stream);
            }
        }
        catch (EndOfStreamException)
        {
            // A truncated header: the right signature with no usable structure behind it.
        }
        finally
        {
            stream.Position = 0;
        }

        return signature[0] == 0xFF && signature[1] == 0xD8 ? Unreadable(MediaImageFormat.Jpeg)
            : signature[..8].SequenceEqual(PngSignature) ? Unreadable(MediaImageFormat.Png)
            : signature[..4].SequenceEqual("RIFF"u8) && signature[8..12].SequenceEqual("WEBP"u8) ? Unreadable(MediaImageFormat.Webp)
            : null;
    }

    internal static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ImageProbeResult Unreadable(MediaImageFormat format) =>
        new(format, 0, 0, ExifMetadata.None, IsAnimated: false, IsLossless: false, HasAlpha: false);

    private static ImageProbeResult ProbeJpeg(Stream stream)
    {
        stream.Position = 2;
        int width = 0, height = 0;
        var exif = ExifMetadata.None;

        for (var seen = 0; ; seen++)
        {
            if (seen > MaxStructures)
            {
                throw new EndOfStreamException("Too many segments.");
            }

            var marker = NextMarker(stream);
            if (marker is 0xD9 or -1)
            {
                break;
            }

            // Standalone markers carry no length.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                continue;
            }

            var length = ReadUInt16BigEndian(stream);
            if (length < 2)
            {
                break;
            }

            var payloadStart = stream.Position;
            var payloadLength = length - 2;

            if (IsStartOfFrame(marker) && payloadLength >= 5)
            {
                var frame = ReadExactly(stream, 5);
                height = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(1));
                width = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(3));
            }
            else if (marker == 0xE1 && payloadLength > 6)
            {
                var payload = ReadExactly(stream, payloadLength);
                if (payload.AsSpan().StartsWith("Exif\0\0"u8))
                {
                    exif = ExifMetadata.Read(payload.AsSpan(6));
                }
            }

            // Start of scan: everything after it is pixels, and every header field has been seen.
            if (marker == 0xDA)
            {
                break;
            }

            stream.Position = payloadStart + payloadLength;
        }

        return new(MediaImageFormat.Jpeg, width, height, exif, IsAnimated: false, IsLossless: false, HasAlpha: false);
    }

    private static ImageProbeResult ProbePng(Stream stream)
    {
        stream.Position = 8;
        int width = 0, height = 0;
        bool animated = false, alpha = false;
        var exif = ExifMetadata.None;

        for (var seen = 0; stream.Position + 8 <= stream.Length; seen++)
        {
            if (seen > MaxStructures)
            {
                throw new EndOfStreamException("Too many chunks.");
            }

            var length = ReadUInt32BigEndian(stream);
            var type = ReadExactly(stream, 4);
            var dataStart = stream.Position;

            if (type.AsSpan().SequenceEqual("IHDR"u8) && length >= 13)
            {
                var header = ReadExactly(stream, 13);
                width = ClampToInt(BinaryPrimitives.ReadUInt32BigEndian(header));
                height = ClampToInt(BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));

                // Colour types 4 (grey + alpha) and 6 (RGBA) carry an alpha channel.
                alpha |= header[9] is 4 or 6;
            }
            else if (type.AsSpan().SequenceEqual("acTL"u8))
            {
                animated = true;
            }
            else if (type.AsSpan().SequenceEqual("tRNS"u8))
            {
                alpha = true;
            }
            else if (type.AsSpan().SequenceEqual("eXIf"u8) && length <= MaxExifBytes)
            {
                exif = ExifMetadata.Read(ReadExactly(stream, (int)length));
            }
            else if (type.AsSpan().SequenceEqual("IEND"u8))
            {
                break;
            }

            stream.Position = dataStart + length + 4;
        }

        return new(MediaImageFormat.Png, width, height, exif, animated, IsLossless: true, alpha);
    }

    private static ImageProbeResult ProbeWebp(Stream stream)
    {
        stream.Position = 4;
        var riffEnd = Math.Min(stream.Length, 8L + ReadUInt32LittleEndian(stream));
        stream.Position = 12;

        int width = 0, height = 0;
        bool animated = false, alpha = false, lossless = false, extended = false;
        var exif = ExifMetadata.None;

        for (var seen = 0; stream.Position + 8 <= riffEnd; seen++)
        {
            if (seen > MaxStructures)
            {
                throw new EndOfStreamException("Too many chunks.");
            }

            var fourCc = ReadExactly(stream, 4);
            var size = ReadUInt32LittleEndian(stream);
            var dataStart = stream.Position;
            var chunk = fourCc.AsSpan();

            if (chunk.SequenceEqual("VP8X"u8) && size >= 10)
            {
                var header = ReadExactly(stream, 10);
                extended = true;
                animated = (header[0] & 0x02) != 0;
                alpha = (header[0] & 0x10) != 0;
                width = 1 + (header[4] | (header[5] << 8) | (header[6] << 16));
                height = 1 + (header[7] | (header[8] << 8) | (header[9] << 16));
            }
            else if (chunk.SequenceEqual("VP8 "u8) && size >= 10)
            {
                var header = ReadExactly(stream, 10);
                if (!extended && header[3] == 0x9D && header[4] == 0x01 && header[5] == 0x2A)
                {
                    width = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6)) & 0x3FFF;
                    height = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) & 0x3FFF;
                }
            }
            else if (chunk.SequenceEqual("VP8L"u8) && size >= 5)
            {
                var header = ReadExactly(stream, 5);
                lossless = true;
                if (header[0] == 0x2F)
                {
                    var bits = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(1));
                    alpha |= ((bits >> 28) & 1) != 0;
                    if (!extended)
                    {
                        width = (int)(bits & 0x3FFF) + 1;
                        height = (int)((bits >> 14) & 0x3FFF) + 1;
                    }
                }
            }
            else if (chunk.SequenceEqual("ANIM"u8) || chunk.SequenceEqual("ANMF"u8))
            {
                animated = true;
            }
            else if (chunk.SequenceEqual("ALPH"u8))
            {
                alpha = true;
            }
            else if (chunk.SequenceEqual("EXIF"u8) && size <= MaxExifBytes)
            {
                exif = ExifMetadata.Read(ReadExactly(stream, (int)size));
            }

            stream.Position = dataStart + size + (size & 1);
        }

        return new(MediaImageFormat.Webp, width, height, exif, animated, lossless, alpha);
    }

    /// <summary>The next JPEG marker's code, skipping fill bytes; -1 at the end of the stream or on garbage.</summary>
    internal static int NextMarker(Stream stream)
    {
        var value = stream.ReadByte();
        if (value != 0xFF)
        {
            return -1;
        }

        do
        {
            value = stream.ReadByte();
        }
        while (value == 0xFF);

        return value;
    }

    /// <summary>SOF0–SOF15, less DHT (C4), JPG (C8) and DAC (CC), which share the range.</summary>
    internal static bool IsStartOfFrame(int marker) =>
        marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    internal static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        stream.ReadExactly(buffer);
        return buffer;
    }

    internal static int ReadUInt16BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    internal static uint ReadUInt32BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    internal static uint ReadUInt32LittleEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private static int ClampToInt(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
