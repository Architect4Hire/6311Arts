using System.Buffers.Binary;

namespace Arts6311.Domain.Modules.Media.Managers.Imaging;

/// <summary>
/// Copies an image with every metadata container removed except an allow-list, and writes the kept EXIF back in
/// (ADR 0030). Lossless: the compressed pixel data is copied byte for byte and never decoded.
/// </summary>
/// <remarks>
/// <para>What survives, per container:</para>
/// <list type="bullet">
///   <item>JPEG: the JFIF header, ICC colour profile, Adobe colour-transform marker and every coding segment. Dropped:
///   EXIF and XMP (APP1), Photoshop/IPTC (APP13), MPF secondary images (APP2) and anything after the main image's
///   end marker, where phones append depth maps and gain maps that carry their own EXIF.</item>
///   <item>PNG: critical chunks and colour/geometry chunks (<c>iCCP</c>, <c>sRGB</c>, <c>gAMA</c>, <c>cHRM</c>,
///   <c>pHYs</c>, …). Dropped: <c>eXIf</c>, every text chunk (XMP travels in <c>iTXt</c>), <c>tIME</c>, and unknown
///   ancillary chunks.</item>
///   <item>WebP: the image data, alpha and ICC profile. Dropped: <c>EXIF</c>, <c>XMP </c> and unknown chunks.</item>
/// </list>
/// <para>Then <see cref="ExifMetadata.ToTiff"/> is written back in the container's own framing.</para>
/// </remarks>
public static class ImageMetadataRewriter
{
    private static readonly HashSet<string> KeptPngChunks = new(StringComparer.Ordinal)
    {
        "IHDR", "PLTE", "IDAT", "IEND", "tRNS", "cHRM", "gAMA", "iCCP", "sBIT", "sRGB", "cICP", "mDCV", "cLLI",
        "bKGD", "hIST", "pHYs", "sPLT",
    };

    private static readonly HashSet<string> KeptWebpChunks = new(StringComparer.Ordinal)
    {
        "VP8 ", "VP8L", "ALPH", "ICCP",
    };

    /// <param name="source">Seekable; read from its start.</param>
    /// <param name="destination">Seekable and empty; the WebP RIFF size is patched in once the length is known.</param>
    /// <exception cref="InvalidDataException">The structure is broken; the caller treats the file as undecodable.</exception>
    public static void Rewrite(Stream source, MediaImageFormat format, ExifMetadata keep, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(destination);

        source.Position = 0;
        try
        {
            switch (format)
            {
                case MediaImageFormat.Jpeg:
                    RewriteJpeg(source, keep.ToTiff(), destination);
                    break;
                case MediaImageFormat.Png:
                    RewritePng(source, keep.ToTiff(), destination);
                    break;
                case MediaImageFormat.Webp:
                    RewriteWebp(source, keep.ToTiff(), destination);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException("The image ends before its structure does.", exception);
        }
    }

    private static void RewriteJpeg(Stream source, byte[]? tiff, Stream destination)
    {
        if (ImageProbe.NextMarker(source) != 0xD8)
        {
            throw new InvalidDataException("Not a JPEG.");
        }

        destination.Write([0xFF, 0xD8]);
        var exifWritten = tiff is null;

        for (var seen = 0; ; seen++)
        {
            Guard(seen);
            var marker = ImageProbe.NextMarker(source);
            if (marker == -1)
            {
                throw new InvalidDataException("A JPEG segment is malformed.");
            }

            if (marker == 0xD9)
            {
                destination.Write([0xFF, 0xD9]);
                return;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                destination.Write([0xFF, (byte)marker]);
                continue;
            }

            var length = ImageProbe.ReadUInt16BigEndian(source);
            if (length < 2)
            {
                throw new InvalidDataException("A JPEG segment is malformed.");
            }

            var payload = ImageProbe.ReadExactly(source, length - 2);

            // The kept EXIF goes straight after the JFIF header, or first if there is none.
            if (!exifWritten && !(marker == 0xE0 && IsJfif(payload)))
            {
                WriteJpegExif(destination, tiff!);
                exifWritten = true;
            }

            if (KeepJpegSegment(marker, payload))
            {
                WriteJpegSegment(destination, marker, payload);
            }

            if (marker == 0xDA)
            {
                CopyEntropyCodedData(source, destination);
                return;
            }
        }
    }

    /// <summary>
    /// Copies from just after a start-of-scan header to the main image's end marker. Within entropy-coded data a
    /// 0xFF is always followed by 0x00 or a restart marker, so the first other marker is structural: between the
    /// scans of a progressive JPEG it is a table or the next scan, and at the end it is EOI. Anything after EOI is
    /// not copied.
    /// </summary>
    private static void CopyEntropyCodedData(Stream source, Stream destination)
    {
        // Byte at a time over FileStreams, which buffer internally; the scan has to see every byte anyway.
        var output = destination;
        var segments = 0;
        while (true)
        {
            var value = source.ReadByte();
            if (value == -1)
            {
                // No end marker: decoders accept this, so a missing EOI is supplied rather than refused.
                output.Write([0xFF, 0xD9]);
                return;
            }

            if (value != 0xFF)
            {
                output.WriteByte((byte)value);
                continue;
            }

            var next = source.ReadByte();
            while (next == 0xFF)
            {
                next = source.ReadByte();
            }

            if (next is 0x00 or (>= 0xD0 and <= 0xD7))
            {
                output.WriteByte(0xFF);
                output.WriteByte((byte)next);
                continue;
            }

            if (next is 0xD9 or -1)
            {
                output.Write([0xFF, 0xD9]);
                return;
            }

            // A segment between scans (DHT, DQT, DRI, SOS, or an APPn/COM some encoders place here).
            Guard(++segments);
            var length = ImageProbe.ReadUInt16BigEndian(source);
            if (length < 2)
            {
                throw new InvalidDataException("A JPEG segment is malformed.");
            }

            var payload = ImageProbe.ReadExactly(source, length - 2);
            if (KeepJpegSegment(next, payload))
            {
                WriteJpegSegment(output, next, payload);
            }
        }
    }

    /// <summary>
    /// An allow-list here too: the segments that code the image, and the three metadata segments that colour it. A
    /// reserved or extension marker (JPGn, F0–FD) is never copied, so nothing can ride along in one.
    /// </summary>
    private static bool KeepJpegSegment(int marker, byte[] payload) => marker switch
    {
        0xE0 => IsJfif(payload),
        0xE2 => payload.AsSpan().StartsWith("ICC_PROFILE\0"u8),
        0xEE => payload.AsSpan().StartsWith("Adobe"u8),

        // DHT, DAC, DQT, DNL, DRI, DHP, EXP, SOS, and every start-of-frame (C0–CF less C4/C8/CC).
        0xC4 or 0xCC or 0xDB or 0xDC or 0xDD or 0xDE or 0xDF or 0xDA => true,
        _ => ImageProbe.IsStartOfFrame(marker),
    };

    private static void Guard(int seen)
    {
        if (seen > ImageProbe.MaxStructures)
        {
            throw new InvalidDataException("The image has more segments or chunks than any encoder writes.");
        }
    }

    private static bool IsJfif(byte[] payload) => payload.AsSpan().StartsWith("JFIF\0"u8);

    private static void WriteJpegExif(Stream destination, byte[] tiff)
    {
        var payload = new byte[6 + tiff.Length];
        "Exif\0\0"u8.CopyTo(payload);
        tiff.CopyTo(payload, 6);
        WriteJpegSegment(destination, 0xE1, payload);
    }

    private static void WriteJpegSegment(Stream destination, int marker, byte[] payload)
    {
        Span<byte> header = stackalloc byte[4];
        header[0] = 0xFF;
        header[1] = (byte)marker;
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], checked((ushort)(payload.Length + 2)));
        destination.Write(header);
        destination.Write(payload);
    }

    private static void RewritePng(Stream source, byte[]? tiff, Stream destination)
    {
        var signature = ImageProbe.ReadExactly(source, 8);
        if (!signature.AsSpan().SequenceEqual(ImageProbe.PngSignature))
        {
            throw new InvalidDataException("Not a PNG.");
        }

        destination.Write(signature);
        var exifWritten = tiff is null;
        Span<byte> header = stackalloc byte[8];

        for (var seen = 0; ; seen++)
        {
            Guard(seen);
            var length = ImageProbe.ReadUInt32BigEndian(source);
            if (length > int.MaxValue)
            {
                throw new InvalidDataException("A PNG chunk is malformed.");
            }

            var typeBytes = ImageProbe.ReadExactly(source, 4);
            var type = System.Text.Encoding.ASCII.GetString(typeBytes);

            // Critical chunks start with an upper-case letter. One we do not know cannot be skipped safely.
            var critical = char.IsUpper(type[0]);
            if (critical && !KeptPngChunks.Contains(type))
            {
                throw new InvalidDataException("The PNG has a critical chunk this system does not know.");
            }

            if (!exifWritten && type == "IDAT")
            {
                WritePngChunk(destination, "eXIf"u8, tiff!);
                exifWritten = true;
            }

            if (KeptPngChunks.Contains(type))
            {
                // Copied with its original CRC: the bytes are unchanged.
                BinaryPrimitives.WriteUInt32BigEndian(header, length);
                typeBytes.CopyTo(header[4..]);
                destination.Write(header);
                CopyExactly(source, destination, length + 4);
            }
            else
            {
                source.Position += length + 4;
            }

            if (type == "IEND")
            {
                return;
            }
        }
    }

    private static void WritePngChunk(Stream destination, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        type.CopyTo(header[4..]);
        destination.Write(header);
        destination.Write(data);

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, PngCrc.Compute(type, data));
        destination.Write(checksum);
    }

    private static void RewriteWebp(Stream source, byte[]? tiff, Stream destination)
    {
        var header = ImageProbe.ReadExactly(source, 12);
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            throw new InvalidDataException("Not a WebP.");
        }

        var riffEnd = Math.Min(source.Length, 8L + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)));

        // Gathered first: a simple-format file that gains EXIF needs a VP8X header built from its image chunk.
        var chunks = new List<(string FourCc, long Offset, uint Size)>();
        byte[]? vp8x = null;
        for (var seen = 0; source.Position + 8 <= riffEnd; seen++)
        {
            Guard(seen);
            var fourCc = System.Text.Encoding.ASCII.GetString(ImageProbe.ReadExactly(source, 4));
            var size = ImageProbe.ReadUInt32LittleEndian(source);
            var offset = source.Position;
            if (offset + size > riffEnd)
            {
                throw new InvalidDataException("A WebP chunk is malformed.");
            }

            if (fourCc == "VP8X" && size >= 10)
            {
                vp8x = ImageProbe.ReadExactly(source, (int)size);
            }
            else if (KeptWebpChunks.Contains(fourCc) && !chunks.Exists(chunk => chunk.FourCc == fourCc))
            {
                // One of each: a still image has exactly one image chunk, one ALPH and one ICCP.
                chunks.Add((fourCc, offset, size));
            }

            source.Position = offset + size + (size & 1);
        }

        if (!chunks.Exists(chunk => chunk.FourCc is "VP8 " or "VP8L"))
        {
            throw new InvalidDataException("The WebP has no image data.");
        }

        destination.Write("RIFF\0\0\0\0WEBP"u8);

        var needsExtended = vp8x is not null || tiff is not null || chunks.Exists(chunk => chunk.FourCc is "ICCP" or "ALPH");
        if (needsExtended)
        {
            vp8x ??= SynthesizeVp8x(source, chunks);

            // Flags: ICC (0x20) and alpha (0x10) as the image has them; XMP (0x04) never; EXIF (0x08) when written.
            vp8x[0] = (byte)((vp8x[0] & ~(0x04 | 0x08)) | (tiff is null ? 0 : 0x08));
            if (chunks.Exists(chunk => chunk.FourCc == "ICCP"))
            {
                vp8x[0] |= 0x20;
            }
            else
            {
                vp8x[0] = (byte)(vp8x[0] & ~0x20);
            }

            WriteWebpChunk(destination, "VP8X"u8, vp8x);
        }

        // Container order (WebP spec): VP8X, ICCP, ALPH, image data, EXIF.
        Span<byte> chunkHeader = stackalloc byte[8];
        foreach (var chunk in chunks.OrderBy(chunk => chunk.FourCc switch { "ICCP" => 0, "ALPH" => 1, _ => 2 }))
        {
            System.Text.Encoding.ASCII.GetBytes(chunk.FourCc, chunkHeader);
            BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader[4..], chunk.Size);
            destination.Write(chunkHeader);
            source.Position = chunk.Offset;
            CopyExactly(source, destination, chunk.Size);
            if ((chunk.Size & 1) != 0)
            {
                destination.WriteByte(0);
            }
        }

        if (tiff is not null)
        {
            WriteWebpChunk(destination, "EXIF"u8, tiff);
        }

        var end = destination.Position;
        destination.Position = 4;
        Span<byte> riffSize = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(riffSize, checked((uint)(end - 8)));
        destination.Write(riffSize);
        destination.Position = end;
    }

    /// <summary>A VP8X header for a simple-format file: the canvas size and alpha flag, read from its image chunk.</summary>
    private static byte[] SynthesizeVp8x(Stream source, List<(string FourCc, long Offset, uint Size)> chunks)
    {
        var image = chunks.First(chunk => chunk.FourCc is "VP8 " or "VP8L");
        source.Position = image.Offset;
        int width, height;
        var alpha = chunks.Exists(chunk => chunk.FourCc == "ALPH");

        if (image.FourCc == "VP8L")
        {
            var bits = ImageProbe.ReadExactly(source, 5);
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(bits.AsSpan(1));
            width = (int)(packed & 0x3FFF) + 1;
            height = (int)((packed >> 14) & 0x3FFF) + 1;
            alpha |= ((packed >> 28) & 1) != 0;
        }
        else
        {
            var frame = ImageProbe.ReadExactly(source, 10);
            width = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(8)) & 0x3FFF;
        }

        var vp8x = new byte[10];
        vp8x[0] = (byte)(alpha ? 0x10 : 0);
        WriteUInt24(vp8x.AsSpan(4), width - 1);
        WriteUInt24(vp8x.AsSpan(7), height - 1);
        return vp8x;
    }

    private static void WriteUInt24(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    private static void WriteWebpChunk(Stream destination, ReadOnlySpan<byte> fourCc, byte[] data)
    {
        Span<byte> header = stackalloc byte[8];
        fourCc.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)data.Length);
        destination.Write(header);
        destination.Write(data);
        if ((data.Length & 1) != 0)
        {
            destination.WriteByte(0);
        }
    }

    private static void CopyExactly(Stream source, Stream destination, long count)
    {
        var buffer = new byte[(int)Math.Min(count, 81920)];
        while (count > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            destination.Write(buffer, 0, read);
            count -= read;
        }
    }
}
