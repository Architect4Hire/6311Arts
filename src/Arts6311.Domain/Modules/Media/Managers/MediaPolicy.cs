namespace Arts6311.Domain.Modules.Media.Managers;

/// <summary>
/// The Media module's fixed limits and encoding settings (media.md, ADR 0030). Constants rather than configuration:
/// they are product decisions with tests behind them, and the gateway's body limit is derived from them.
/// </summary>
public static class MediaPolicy
{
    /// <summary>The largest file a person may upload: 25 MiB. Checked while the upload streams, before anything decodes.</summary>
    public const long MaxUploadBytes = 25L * 1024 * 1024;

    /// <summary>
    /// The request body the API accepts on the upload route: the file plus room for the multipart framing and the
    /// small text fields. The gateway's <c>cms-media-upload</c> route allows the same.
    /// </summary>
    public const long MaxUploadRequestBytes = MaxUploadBytes + (1024 * 1024);

    /// <summary>The longest side an upload may have. Read from the file header, so a decompression bomb never decodes.</summary>
    public const int MaxSidePixels = 12_000;

    /// <summary>The most pixels an upload may have (50 megapixels, about 200 MB decoded).</summary>
    public const long MaxPixels = 50_000_000;

    /// <summary>Smaller than this is not a picture anyone will place on a page; it is usually a tracking pixel or a mistake.</summary>
    public const int MinSidePixels = 16;

    /// <summary>
    /// The stored master's longest side. Uploads larger than this are downscaled before they are stored: 1.6× the
    /// widest rendition, which leaves headroom for a full-width hero on a high-density screen.
    /// </summary>
    public const int MasterMaxLongSide = 2560;

    /// <summary>
    /// JPEG quality for a re-encoded master. With 4:4:4 chroma this measured ≈ 40 dB PSNR on a dense test photo, the
    /// usual threshold for "no visible loss" (ADR 0030).
    /// </summary>
    public const int MasterJpegQuality = 92;

    /// <summary>Lossy WebP quality for a re-encoded master; same reasoning as <see cref="MasterJpegQuality"/>.</summary>
    public const int MasterWebpQuality = 92;

    /// <summary>
    /// When a lossy upload needs no downscaling, the re-encode replaces it only if it saves at least this fraction:
    /// a smaller saving is not worth a second generation of compression.
    /// </summary>
    public const double MasterMinimumLossySaving = 0.10;

    /// <summary>The rendition widths (media.md). Never upscaled: an image narrower than a width is encoded at its own.</summary>
    public static readonly IReadOnlyList<int> RenditionWidths = [400, 800, 1600];

    public const int AltTextMaxLength = 500;

    public const int CreditMaxLength = 200;

    public const int FileNameMaxLength = 255;

    /// <summary>The rendition token that names the master itself. Served to CMS sessions only.</summary>
    public const string OriginalRendition = "original";

    /// <summary>The blob container (AppHost declares it; nobody creates it).</summary>
    public const string ContainerName = "media";

    /// <summary>Lossy rendition qualities per compression preset.</summary>
    public static RenditionQuality QualityFor(MediaCompressionPreset preset) => preset switch
    {
        MediaCompressionPreset.HighQuality => new(WebpQuality: 88, JpegQuality: 90, JpegChroma444: true),
        MediaCompressionPreset.Smallest => new(WebpQuality: 70, JpegQuality: 74, JpegChroma444: false),
        _ => new(WebpQuality: 80, JpegQuality: 82, JpegChroma444: false),
    };
}

/// <summary>How hard a preset compresses its renditions.</summary>
public sealed record RenditionQuality(int WebpQuality, int JpegQuality, bool JpegChroma444);
