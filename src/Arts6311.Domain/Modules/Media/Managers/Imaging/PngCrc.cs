namespace Arts6311.Domain.Modules.Media.Managers.Imaging;

/// <summary>
/// The CRC-32 a PNG chunk carries (ISO 3309, reflected polynomial 0xEDB88320), over the chunk type and data. Here
/// rather than System.IO.Hashing, which is a separate package, for the one chunk this system ever writes.
/// </summary>
internal static class PngCrc
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        crc = Update(crc, type);
        crc = Update(crc, data);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
