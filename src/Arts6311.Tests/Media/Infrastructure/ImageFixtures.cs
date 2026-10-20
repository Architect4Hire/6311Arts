using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Arts6311.Tests.Media.Infrastructure;

/// <summary>
/// Test images with the metadata a phone or camera writes — a GPS position, a camera make, XMP — assembled by hand
/// here rather than by the code under test, so a bug in the rewriter cannot also be a bug in its own fixtures.
/// </summary>
internal static class ImageFixtures
{
    /// <summary>The camera make the fixtures write. Its presence in a stored file means metadata leaked.</summary>
    public const string LeakyMake = "LEAKY-CAMERA-MAKE";

    /// <summary>Text the XMP packets carry, including a location. Same rule.</summary>
    public const string LeakyXmp = "exif:GPSLatitude=41,59.1234N LEAKY-XMP";

    public const string Copyright = "(c) 2026 Ada Artist";

    public const string Artist = "Ada Artist";

    /// <summary>The GPS latitude seconds the fixtures write as a rational: 1234/100.</summary>
    public static readonly byte[] GpsSecondsRational = [0x00, 0x00, 0x04, 0xD2, 0x00, 0x00, 0x00, 0x64];

    /// <summary>A picture with structure: a gradient, and a red block in the top-left corner to find orientation by.</summary>
    public static SKBitmap Picture(int width, int height, bool alpha = false)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, alpha ? SKAlphaType.Unpremul : SKAlphaType.Opaque));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = new SKColor((byte)(x * 255 / Math.Max(1, width - 1)), (byte)(y * 255 / Math.Max(1, height - 1)), 128,
                    alpha ? (byte)(x % 2 == 0 ? 255 : 96) : (byte)255);
                bitmap.SetPixel(x, y, color);
            }
        }

        var block = Math.Max(2, Math.Min(width, height) / 8);
        for (var y = 0; y < block; y++)
        {
            for (var x = 0; x < block; x++)
            {
                bitmap.SetPixel(x, y, new SKColor(255, 0, 0));
            }
        }

        return bitmap;
    }

    public static byte[] Jpeg(int width, int height, int quality = 90, byte[]? exifTiff = null, bool xmp = false, byte[]? trailing = null)
    {
        using var bitmap = Picture(width, height);
        var encoded = Encode(bitmap, SKEncodedImageFormat.Jpeg, quality);

        // Segments go straight after SOI, in the order a camera writes them: EXIF, then XMP.
        using var output = new MemoryStream();
        output.Write(encoded, 0, 2);
        if (exifTiff is not null)
        {
            WriteJpegSegment(output, 0xE1, [.. "Exif\0\0"u8, .. exifTiff]);
        }

        if (xmp)
        {
            WriteJpegSegment(output, 0xE1, [.. "http://ns.adobe.com/xap/1.0/\0"u8, .. Encoding.UTF8.GetBytes(XmpPacket())]);
        }

        // A comment and a Photoshop/IPTC block, both of which can carry a location by hand.
        WriteJpegSegment(output, 0xFE, Encoding.ASCII.GetBytes("comment " + LeakyMake));
        WriteJpegSegment(output, 0xED, [.. "Photoshop 3.0\0"u8, .. Encoding.ASCII.GetBytes(LeakyMake)]);
        output.Write(encoded, 2, encoded.Length - 2);
        if (trailing is not null)
        {
            output.Write(trailing);
        }

        return output.ToArray();
    }

    public static byte[] Png(int width, int height, bool alpha = false, byte[]? exifTiff = null, bool xmp = false)
    {
        using var bitmap = Picture(width, height, alpha);
        var encoded = Encode(bitmap, SKEncodedImageFormat.Png, 100);

        using var output = new MemoryStream();
        var position = 8;
        output.Write(encoded, 0, 8);
        var inserted = false;
        while (position < encoded.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(position));
            var type = Encoding.ASCII.GetString(encoded, position + 4, 4);
            if (type == "IDAT" && !inserted)
            {
                if (exifTiff is not null)
                {
                    WritePngChunk(output, "eXIf", exifTiff);
                }

                if (xmp)
                {
                    WritePngChunk(output, "iTXt", [.. "XML:com.adobe.xmp\0\0\0\0\0"u8, .. Encoding.UTF8.GetBytes(XmpPacket())]);
                }

                WritePngChunk(output, "tEXt", [.. "Comment\0"u8, .. Encoding.ASCII.GetBytes(LeakyMake)]);
                inserted = true;
            }

            output.Write(encoded, position, length + 12);
            position += length + 12;
        }

        return output.ToArray();
    }

    public static byte[] Webp(int width, int height, bool lossless = false, byte[]? exifTiff = null, bool xmp = false)
    {
        using var bitmap = Picture(width, height);
        var encoded = lossless
            ? EncodeWebpLossless(bitmap)
            : Encode(bitmap, SKEncodedImageFormat.Webp, 90);

        if (exifTiff is null && !xmp)
        {
            return encoded;
        }

        // Metadata needs the extended (VP8X) container. Skia writes the simple one for a bitmap with no colour profile,
        // so the header is built here, as a camera's encoder would; metadata chunks then go after the image data.
        using var output = new MemoryStream();
        if (Encoding.ASCII.GetString(encoded, 12, 4) == "VP8X")
        {
            output.Write(encoded);
        }
        else
        {
            var vp8x = new byte[10];
            vp8x[4] = (byte)(width - 1);
            vp8x[5] = (byte)((width - 1) >> 8);
            vp8x[6] = (byte)((width - 1) >> 16);
            vp8x[7] = (byte)(height - 1);
            vp8x[8] = (byte)((height - 1) >> 8);
            vp8x[9] = (byte)((height - 1) >> 16);
            output.Write(encoded, 0, 12);
            WriteWebpChunk(output, "VP8X", vp8x);
            output.Write(encoded, 12, encoded.Length - 12);
        }

        if (exifTiff is not null)
        {
            WriteWebpChunk(output, "EXIF", exifTiff);
            output.GetBuffer()[20] |= 0x08;
        }

        if (xmp)
        {
            WriteWebpChunk(output, "XMP ", Encoding.UTF8.GetBytes(XmpPacket()));
            output.GetBuffer()[20] |= 0x04;
        }

        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        return bytes;
    }

    /// <summary>
    /// A TIFF structure as a camera writes it: make, orientation, artist, copyright, and a GPS IFD with a latitude.
    /// </summary>
    public static byte[] ExifWithGps(int orientation = 1, bool bigEndian = false, string? copyright = Copyright, string? artist = Artist)
    {
        var entries = new List<(ushort Tag, ushort Type, uint Count, byte[] Value)>
        {
            (0x010F, 2, 0, Ascii(LeakyMake)),
            (0x0112, 3, 1, U16(orientation, bigEndian)),
        };
        if (artist is not null)
        {
            entries.Add((0x013B, 2, 0, Ascii(artist)));
        }

        if (copyright is not null)
        {
            entries.Add((0x8298, 2, 0, Ascii(copyright)));
        }

        // GPS IFD pointer; the offset is patched once the layout is known.
        entries.Add((0x8825, 4, 1, new byte[4]));

        using var tiff = new MemoryStream();
        tiff.Write(bigEndian ? "MM\0*"u8 : "II*\0"u8);
        tiff.Write(U32(8, bigEndian));

        var ifdLength = 2 + (entries.Count * 12) + 4;
        var dataOffset = 8 + ifdLength;
        var data = new MemoryStream();
        var table = new MemoryStream();
        table.Write(U16(entries.Count, bigEndian));
        var gpsPointerAt = 0;
        foreach (var (tag, type, count, value) in entries)
        {
            table.Write(U16(tag, bigEndian));
            table.Write(U16(type, bigEndian));
            table.Write(U32(type == 2 ? (uint)value.Length : count, bigEndian));
            if (tag == 0x8825)
            {
                gpsPointerAt = 8 + (int)table.Position;
            }

            if (value.Length <= 4)
            {
                table.Write([.. value, .. new byte[4 - value.Length]]);
            }
            else
            {
                table.Write(U32((uint)(dataOffset + data.Position), bigEndian));
                data.Write(value);
                if (value.Length % 2 == 1)
                {
                    data.WriteByte(0);
                }
            }
        }

        table.Write(U32(0, bigEndian));
        tiff.Write(table.ToArray());
        tiff.Write(data.ToArray());

        // The GPS IFD: latitude ref "N" and latitude 41°59'12.34".
        var gpsIfd = (uint)tiff.Position;
        var rationals = (uint)(gpsIfd + 2 + (2 * 12) + 4);
        tiff.Write(U16(2, bigEndian));
        tiff.Write(U16(0x0001, bigEndian));
        tiff.Write(U16(2, bigEndian));
        tiff.Write(U32(2, bigEndian));
        tiff.Write("N\0\0\0"u8);
        tiff.Write(U16(0x0002, bigEndian));
        tiff.Write(U16(5, bigEndian));
        tiff.Write(U32(3, bigEndian));
        tiff.Write(U32(rationals, bigEndian));
        tiff.Write(U32(0, bigEndian));
        tiff.Write([.. U32(41, true), .. U32(1, true), .. U32(59, true), .. U32(1, true)]);
        tiff.Write(GpsSecondsRational);

        var bytes = tiff.ToArray();
        U32(gpsIfd, bigEndian).CopyTo(bytes, gpsPointerAt);
        return bytes;
    }

    /// <summary>Whether the bytes still carry anything the fixtures planted as leaky metadata.</summary>
    public static bool ContainsLeak(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        return text.Contains(LeakyMake, StringComparison.Ordinal)
            || text.Contains("LEAKY-XMP", StringComparison.Ordinal)
            || bytes.AsSpan().IndexOf(GpsSecondsRational) >= 0;
    }

    public static SKBitmap Decode(byte[] bytes) =>
        SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("The fixture did not decode.");

    /// <summary>Unpremultiplied RGBA pixels, for exact comparisons.</summary>
    public static uint[] Pixels(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes)) ?? throw new InvalidOperationException("No codec.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888,
            codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels()));
        return [.. System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bitmap.GetPixelSpan())];
    }

    /// <summary>An SVG with a script in it: the upload every image endpoint must refuse.</summary>
    public static byte[] Svg() => Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><script>alert(1)</script></svg>");

    /// <summary>
    /// A PNG whose header claims enormous dimensions and that has no pixels at all: if anything decoded it before
    /// checking the header, it would try to allocate gigabytes.
    /// </summary>
    public static byte[] PngHeaderOnly(uint width, uint height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WritePngChunk(output, "IHDR", ihdr);
        WritePngChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>An animated PNG: a valid PNG with an <c>acTL</c> chunk.</summary>
    public static byte[] AnimatedPng()
    {
        var png = Png(32, 32);
        using var output = new MemoryStream();
        output.Write(png, 0, 8 + 25);
        WritePngChunk(output, "acTL", [0, 0, 0, 2, 0, 0, 0, 0]);
        output.Write(png, 8 + 25, png.Length - 8 - 25);
        return output.ToArray();
    }

    public static bool IsValidPngCrc(byte[] png)
    {
        var position = 8;
        while (position < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position + 8 + length));
            if (crc != Crc32(png.AsSpan(position + 4, length + 4)))
            {
                return false;
            }

            position += length + 12;
        }

        return true;
    }

    public static List<string> PngChunks(byte[] png)
    {
        var chunks = new List<string>();
        var position = 8;
        while (position < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            chunks.Add(Encoding.ASCII.GetString(png, position + 4, 4));
            position += length + 12;
        }

        return chunks;
    }

    public static List<string> WebpChunks(byte[] webp)
    {
        var chunks = new List<string>();
        var position = 12;
        while (position + 8 <= webp.Length)
        {
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(position + 4));
            chunks.Add(Encoding.ASCII.GetString(webp, position, 4));
            position += 8 + size + (size & 1);
        }

        return chunks;
    }

    private static string XmpPacket() =>
        $"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF><rdf:Description {LeakyXmp}/></rdf:RDF></x:xmpmeta>";

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality) ?? throw new InvalidOperationException("Encode failed.");
        return data.ToArray();
    }

    private static byte[] EncodeWebpLossless(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 50))
            ?? throw new InvalidOperationException("Encode failed.");
        return data.ToArray();
    }

    private static void WriteJpegSegment(Stream output, byte marker, byte[] payload)
    {
        output.Write([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)]);
        output.Write(payload);
    }

    private static void WritePngChunk(Stream output, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(U32((uint)data.Length, bigEndian: true));
        output.Write(typeBytes);
        output.Write(data);
        output.Write(U32(Crc32([.. typeBytes, .. data]), bigEndian: true));
    }

    private static void WriteWebpChunk(Stream output, string fourCc, byte[] data)
    {
        output.Write(Encoding.ASCII.GetBytes(fourCc));
        output.Write(U32((uint)data.Length, bigEndian: false));
        output.Write(data);
        if (data.Length % 2 == 1)
        {
            output.WriteByte(0);
        }
    }

    /// <summary>Bitwise CRC-32, deliberately not the table-driven one the code under test uses.</summary>
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static byte[] Ascii(string text) => [.. Encoding.ASCII.GetBytes(text), 0];

    private static byte[] U16(int value, bool bigEndian)
    {
        var bytes = new byte[2];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        }

        return bytes;
    }

    private static byte[] U32(uint value, bool bigEndian)
    {
        var bytes = new byte[4];
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }

        return bytes;
    }
}
