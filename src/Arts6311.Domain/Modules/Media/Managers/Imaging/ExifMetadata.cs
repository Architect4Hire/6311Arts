using System.Buffers.Binary;
using System.Text;

namespace Arts6311.Domain.Modules.Media.Managers.Imaging;

/// <summary>
/// The only EXIF a stored image keeps (media.md "EXIF location data stripped; copyright kept", ADR 0030): who made
/// it, who owns it, and which way up it is. Everything else — GPS above all, but also the camera serial, maker
/// notes and the embedded thumbnail — is dropped by never being copied.
/// </summary>
/// <remarks>
/// An allow-list rather than "remove the GPS IFD": a deny-list has to know every place a location can hide (GPS IFD,
/// XMP, IPTC, maker notes, a thumbnail with its own EXIF), and one forgotten place leaks a home address. Copying
/// three named tags into a fresh TIFF structure cannot leak anything it does not name.
/// </remarks>
public sealed record ExifMetadata(int Orientation, string? Artist, string? Copyright)
{
    public static readonly ExifMetadata None = new(1, null, null);

    private const ushort OrientationTag = 0x0112;
    private const ushort ArtistTag = 0x013B;
    private const ushort CopyrightTag = 0x8298;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;

    /// <summary>The longest Artist or Copyright kept; longer text is cut, never rejected.</summary>
    public const int MaxTextLength = 256;

    /// <summary>Whether writing this would produce any tag at all.</summary>
    public bool IsEmpty => Orientation == 1 && Artist is null && Copyright is null;

    /// <summary>The same metadata once the orientation has been applied to the pixels.</summary>
    public ExifMetadata WithoutOrientation() => this with { Orientation = 1 };

    /// <summary>
    /// Reads the three kept tags from a TIFF structure (the EXIF payload without any <c>Exif\0\0</c> prefix).
    /// Malformed or truncated data yields <see cref="None"/>: metadata is never a reason to refuse a picture.
    /// </summary>
    public static ExifMetadata Read(ReadOnlySpan<byte> tiff)
    {
        if (tiff.StartsWith("Exif\0\0"u8))
        {
            tiff = tiff[6..];
        }

        if (tiff.Length < 8)
        {
            return None;
        }

        bool little;
        if (tiff[0] == 'I' && tiff[1] == 'I')
        {
            little = true;
        }
        else if (tiff[0] == 'M' && tiff[1] == 'M')
        {
            little = false;
        }
        else
        {
            return None;
        }

        if (U16(tiff, 2, little) != 42)
        {
            return None;
        }

        var ifd = U32(tiff, 4, little);
        if (ifd < 8 || ifd > tiff.Length - 2)
        {
            return None;
        }

        var count = U16(tiff, (int)ifd, little);
        var orientation = 1;
        string? artist = null;
        string? copyright = null;

        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + (i * 12);
            if (entry + 12 > tiff.Length)
            {
                break;
            }

            var tag = U16(tiff, entry, little);
            var type = U16(tiff, entry + 2, little);
            var valueCount = U32(tiff, entry + 4, little);

            if (tag == OrientationTag && type == TypeShort && valueCount >= 1)
            {
                var value = U16(tiff, entry + 8, little);
                orientation = value is >= 1 and <= 8 ? value : 1;
            }
            else if ((tag == ArtistTag || tag == CopyrightTag) && type == TypeAscii && valueCount > 0)
            {
                var text = ReadAscii(tiff, entry + 8, valueCount, little);
                if (tag == ArtistTag)
                {
                    artist = text;
                }
                else
                {
                    copyright = text;
                }
            }
        }

        return new ExifMetadata(orientation, artist, copyright);
    }

    /// <summary>
    /// A little-endian TIFF structure carrying only the kept tags, or <c>null</c> when there is nothing to keep.
    /// The caller wraps it in its container's framing (a JPEG APP1, a PNG <c>eXIf</c>, a WebP <c>EXIF</c> chunk).
    /// </summary>
    public byte[]? ToTiff()
    {
        if (IsEmpty)
        {
            return null;
        }

        // Entries in ascending tag order, as TIFF requires.
        var entries = new List<(ushort Tag, ushort Type, byte[] Value)>();
        if (Orientation != 1)
        {
            var value = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(value, (ushort)Orientation);
            entries.Add((OrientationTag, TypeShort, value));
        }

        if (Artist is not null)
        {
            entries.Add((ArtistTag, TypeAscii, AsciiZ(Artist)));
        }

        if (Copyright is not null)
        {
            entries.Add((CopyrightTag, TypeAscii, AsciiZ(Copyright)));
        }

        const int headerLength = 8;
        var ifdLength = 2 + (entries.Count * 12) + 4;
        var dataLength = entries.Where(entry => entry.Value.Length > 4).Sum(entry => entry.Value.Length + (entry.Value.Length % 2));
        var buffer = new byte[headerLength + ifdLength + dataLength];

        "II"u8.CopyTo(buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(headerLength), (ushort)entries.Count);

        var dataOffset = headerLength + ifdLength;
        for (var i = 0; i < entries.Count; i++)
        {
            var (tag, type, value) = entries[i];
            var at = headerLength + 2 + (i * 12);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(
                buffer.AsSpan(at + 4), (uint)(type == TypeShort ? value.Length / 2 : value.Length));

            if (value.Length <= 4)
            {
                value.CopyTo(buffer.AsSpan(at + 8));
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at + 8), (uint)dataOffset);
                value.CopyTo(buffer.AsSpan(dataOffset));
                dataOffset += value.Length + (value.Length % 2);
            }
        }

        // The next-IFD offset after the entries stays zero: there is no IFD1, so no thumbnail.
        return buffer;
    }

    private static string? ReadAscii(ReadOnlySpan<byte> tiff, int valueField, uint count, bool little)
    {
        if (count > 64 * 1024)
        {
            return null;
        }

        ReadOnlySpan<byte> bytes;
        if (count <= 4)
        {
            bytes = tiff.Slice(valueField, (int)count);
        }
        else
        {
            var offset = U32(tiff, valueField, little);
            if (offset > tiff.Length || count > tiff.Length - offset)
            {
                return null;
            }

            bytes = tiff.Slice((int)offset, (int)count);
        }

        var terminator = bytes.IndexOf((byte)0);
        if (terminator >= 0)
        {
            bytes = bytes[..terminator];
        }

        // ASCII by the standard, UTF-8 by common practice; control characters never survive.
        var text = new string([.. Encoding.UTF8.GetString(bytes).Where(character => !char.IsControl(character))]).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    private static byte[] AsciiZ(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text.Length > MaxTextLength ? text[..MaxTextLength] : text);
        var terminated = new byte[bytes.Length + 1];
        bytes.CopyTo(terminated, 0);
        return terminated;
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset, bool little) =>
        offset < 0 || offset + 2 > data.Length ? (ushort)0
        : little ? BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]) : BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static uint U32(ReadOnlySpan<byte> data, int offset, bool little) =>
        offset < 0 || offset + 4 > data.Length ? 0
        : little ? BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
}
