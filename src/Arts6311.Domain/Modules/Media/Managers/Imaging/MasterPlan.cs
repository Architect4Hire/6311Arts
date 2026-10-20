namespace Arts6311.Domain.Modules.Media.Managers.Imaging;

/// <summary>
/// How an upload becomes the stored master (ADR 0030): decided by Business from the probe, carried out by the
/// DataLayer. The master <em>is</em> the original from then on — the upload itself is not kept.
/// </summary>
/// <param name="TargetWidth">The master's displayed width: the upload's own, or less when it is downscaled.</param>
/// <param name="Resize">The upload's longest side is over <see cref="MediaPolicy.MasterMaxLongSide"/>.</param>
/// <param name="Lossless">PNG, or a lossless WebP: re-encoding is pixel-exact.</param>
/// <param name="MinimumSaving">
/// Without a resize, the share of bytes the re-encode must save to replace the (metadata-scrubbed) upload. Zero
/// for lossless formats, where any saving is free; <see cref="MediaPolicy.MasterMinimumLossySaving"/> otherwise.
/// </param>
/// <param name="Keep">The metadata the master carries: artist, copyright, and the orientation if not applied.</param>
public sealed record MasterPlan(
    MediaImageFormat Format,
    int TargetWidth,
    int TargetLongSide,
    bool Resize,
    bool Lossless,
    int Quality,
    bool Chroma444,
    double MinimumSaving,
    ExifMetadata Keep)
{
    /// <summary>A JPEG may decode at a reduced scale when it will be downscaled anyway; otherwise at full size.</summary>
    public int? DecodeLongSide => Resize ? TargetLongSide : null;

    /// <summary>A master keeps its own colour profile; only renditions are converted to sRGB.</summary>
    public static bool ConvertToSrgb => false;

    /// <summary>
    /// Whether the re-encode becomes the master. Always after a resize. Otherwise it must beat the scrubbed upload by
    /// <see cref="MinimumSaving"/>: any saving for a lossless format, 10 % for a lossy one, so a modest JPEG is never
    /// put through a second generation of lossy compression for nothing.
    /// </summary>
    public bool KeepsReencode(long reencodedBytes, long? scrubbedBytes) =>
        Resize || scrubbedBytes is not { } upload || reencodedBytes < upload * (1 - MinimumSaving);

    /// <summary>The plan for an upload; the deterministic half of the master rule.</summary>
    public static MasterPlan For(ImageProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        var longSide = Math.Max(probe.DisplayWidth, probe.DisplayHeight);
        var resize = longSide > MediaPolicy.MasterMaxLongSide;
        var targetWidth = resize
            ? Math.Max(1, (int)Math.Round((double)probe.DisplayWidth * MediaPolicy.MasterMaxLongSide / longSide))
            : probe.DisplayWidth;
        var lossless = probe.Format == MediaImageFormat.Png || (probe.Format == MediaImageFormat.Webp && probe.IsLossless);

        return new MasterPlan(
            probe.Format,
            targetWidth,
            resize ? MediaPolicy.MasterMaxLongSide : longSide,
            resize,
            lossless,
            probe.Format == MediaImageFormat.Webp ? MediaPolicy.MasterWebpQuality : MediaPolicy.MasterJpegQuality,
            Chroma444: true,
            MinimumSaving: lossless ? 0 : MediaPolicy.MasterMinimumLossySaving,
            probe.Exif);
    }
}
