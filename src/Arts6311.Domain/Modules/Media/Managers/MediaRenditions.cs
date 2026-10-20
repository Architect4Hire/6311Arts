using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Arts6311.Domain.Modules.Media.Managers;

/// <summary>A rendition token, <c>{width}.{webp|jpg}</c>: what <c>/media/{id}/{rendition}</c> names.</summary>
public sealed record MediaRenditionName(int Width, MediaImageFormat Format)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}.{Format.Extension()}");

    /// <summary>Every rendition an original gets: each width in WebP and in JPEG (media.md).</summary>
    public static IReadOnlyList<MediaRenditionName> All { get; } =
    [
        .. MediaPolicy.RenditionWidths.SelectMany(width => new[]
        {
            new MediaRenditionName(width, MediaImageFormat.Webp),
            new MediaRenditionName(width, MediaImageFormat.Jpeg),
        }),
    ];

    /// <summary>A known rendition token, or <c>null</c>. Only the widths and formats in <see cref="All"/> exist.</summary>
    public static MediaRenditionName? TryParse(string? token) =>
        All.FirstOrDefault(name => string.Equals(name.ToString(), token, StringComparison.Ordinal));
}

/// <summary>
/// The encoder settings a rendition was made with, as a short hash. Part of each rendition's blob key, so changing
/// a preset (or these settings, by bumping <see cref="Version"/>) produces new keys instead of overwriting files a
/// browser may have cached as immutable.
/// </summary>
public static class RenditionProfile
{
    /// <summary>Bump when the encoder pipeline changes in a way that changes output bytes.</summary>
    private const string Version = "r1";

    public static string For(MediaCompressionPreset preset)
    {
        var quality = MediaPolicy.QualityFor(preset);
        var descriptor = string.Create(
            CultureInfo.InvariantCulture,
            $"{Version}|webp:{quality.WebpQuality}|jpeg:{quality.JpegQuality}:{(quality.JpegChroma444 ? "444" : "420")}|mitchell|srgb");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(descriptor)).AsSpan(0, 4));
    }
}

/// <summary>Where an asset's bytes live in the <c>media</c> container. Everything an asset owns is under its id.</summary>
public static class MediaBlobKeys
{
    public static string Prefix(Guid assetId) => $"{assetId:N}/";

    public static string Original(Guid assetId, string checksum, MediaImageFormat format) =>
        $"{Prefix(assetId)}original-{checksum}.{format.Extension()}";

    /// <summary>Content-addressed: the master's checksum, the rendition and the encoder profile (outbox.md).</summary>
    public static string Rendition(Guid assetId, string masterChecksum, MediaRenditionName rendition, string profile) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix(assetId)}{masterChecksum}/{rendition.Width}-{profile}.{rendition.Format.Extension()}");
}

/// <summary>The URLs the API hands out. Relative: the gateway is the one origin (ADR 0001).</summary>
public static class MediaUrls
{
    /// <summary>
    /// <c>?v=</c> is the first 12 hex digits of the file's checksum. A URL carrying the current version is cached as
    /// immutable; the same path without it, or with an old one, is revalidated.
    /// </summary>
    public static string For(Guid assetId, string rendition, string checksum) =>
        $"/media/{assetId}/{rendition}?v={VersionOf(checksum)}";

    public static string VersionOf(string checksum) => checksum.Length >= 12 ? checksum[..12] : checksum;
}
