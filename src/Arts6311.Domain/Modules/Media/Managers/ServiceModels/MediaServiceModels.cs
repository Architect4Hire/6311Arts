using System.Text.Json.Serialization;

namespace Arts6311.Domain.Modules.Media.Managers.ServiceModels;

/// <summary>One asset in full, as the CMS edits it.</summary>
/// <param name="Bytes">The stored master's size.</param>
/// <param name="UploadedBytes">What was uploaded, before compression.</param>
/// <param name="Width">As displayed (orientation applied).</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
/// <param name="OriginalUrl">The master on <c>/media/**</c>; CMS sessions only.</param>
public sealed record MediaAssetServiceModel(
    Guid Id,
    string? FileName,
    string MimeType,
    long Bytes,
    long? UploadedBytes,
    int Width,
    int Height,
    string Checksum,
    MediaProcessingStatus ProcessingStatus,
    MediaVisibility Visibility,
    MediaCompressionPreset CompressionPreset,
    string? AltText,
    string? Credit,
    string UploadedBy,
    DateTimeOffset UploadedAt,
    DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("etag")] string ETag,
    string OriginalUrl,
    IReadOnlyList<MediaRenditionServiceModel> Renditions);

/// <param name="Name">The rendition token, for example <c>800.webp</c>.</param>
/// <param name="Url">Versioned (<c>?v=</c>), so it may be cached as immutable.</param>
public sealed record MediaRenditionServiceModel(string Name, string MimeType, int Width, int Height, long Bytes, string Url);

/// <summary>One library tile.</summary>
/// <param name="ThumbnailUrl">The 400-wide WebP, or <c>null</c> until renditions exist.</param>
public sealed record MediaAssetSummaryServiceModel(
    Guid Id,
    string? FileName,
    string MimeType,
    long Bytes,
    long? UploadedBytes,
    int Width,
    int Height,
    MediaProcessingStatus ProcessingStatus,
    MediaVisibility Visibility,
    string? AltText,
    string? Credit,
    DateTimeOffset UploadedAt,
    string? ThumbnailUrl);

/// <summary>
/// The answer to "may these assets go on published content?" — for the content modules (2.4 on), which must not
/// publish an image without alt text (media.md).
/// </summary>
public sealed record MediaAttachabilityServiceModel(IReadOnlyList<Guid> Missing, IReadOnlyList<Guid> MissingAltText)
{
    public bool Attachable => Missing.Count == 0 && MissingAltText.Count == 0;
}

/// <summary>How a delivered file may be cached.</summary>
public enum MediaCacheability
{
    /// <summary>A published asset at its current version: cache for a year, never revalidate.</summary>
    Immutable = 0,

    /// <summary>A published asset without (or with an old) <c>?v=</c>: cache briefly, then revalidate with the ETag.</summary>
    Revalidate = 10,

    /// <summary>A draft, served to a CMS session: never stored by a shared cache.</summary>
    Private = 20,
}

/// <summary>
/// An asset's bytes and the headers to send them with. <see cref="Content"/> is a blob stream the caller must
/// dispose; it is <c>null</c> for a 304 or a HEAD.
/// </summary>
public sealed record MediaContentServiceModel(
    Stream? Content,
    string ContentType,
    long Length,
    string ETag,
    MediaCacheability Cacheability,
    bool NotModified);

/// <summary>
/// One picture as the public site shows it: its renditions, never the master (ADR 0030, 0042).
/// </summary>
/// <param name="Src">The widest JPEG rendition, for an <c>&lt;img src&gt;</c> every browser can show.</param>
/// <param name="Alt">The alt text staff wrote. Empty only for an image the content check let through without it.</param>
/// <param name="Width">The original's displayed width, so the page can reserve the picture's shape.</param>
/// <param name="Height">The original's displayed height.</param>
/// <param name="Sources">Every rendition, narrowest first within each format, for <c>srcset</c>.</param>
public sealed record SiteImageServiceModel(
    string Src, string Alt, int Width, int Height, IReadOnlyList<SiteImageSourceServiceModel> Sources);

/// <param name="Url">Versioned (<c>?v=</c>), so it may be cached as immutable.</param>
public sealed record SiteImageSourceServiceModel(string Url, int Width, string MimeType);
