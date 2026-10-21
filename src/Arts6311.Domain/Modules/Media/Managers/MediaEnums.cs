using System.Text.Json.Serialization;

namespace Arts6311.Domain.Modules.Media.Managers;

/// <summary>What an asset is (media.md "Asset kinds"). Stored as its number, so the values never change.</summary>
public enum MediaAssetKind
{
    /// <summary>A staff upload, stored as its compressed master. Immutable once stored.</summary>
    Original = 0,

    /// <summary>Pulled from WordPress by the importer (prompt 4.2).</summary>
    Imported = 10,

    /// <summary>A rendition, linked to its original.</summary>
    Derived = 20,

    /// <summary>
    /// An image an artist sent with the public submission form (prompt 2.12, ADR 0047). Stored and rendered like a
    /// staff upload, but always a draft, never listed in the library, and never a reason to refuse a staff upload
    /// of the same picture.
    /// </summary>
    Submission = 30,
}

/// <summary>Where an original is in its rendition pipeline.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaProcessingStatus>))]
public enum MediaProcessingStatus
{
    /// <summary>Stored; renditions requested and not yet produced.</summary>
    Pending = 0,

    Ready = 10,

    /// <summary>The Worker gave up. Visible in the library, so staff can delete and re-upload.</summary>
    Failed = 20,
}

/// <summary>Who may fetch an asset from <c>/media/**</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaVisibility>))]
public enum MediaVisibility
{
    /// <summary>CMS sessions only. Every asset starts here.</summary>
    Draft = 0,

    /// <summary>
    /// Anyone. Set by content publishing, which arrives with the first content type (2.4/2.8; ADR 0030 DECIDE).
    /// </summary>
    Published = 10,
}

/// <summary>How hard an asset's renditions are compressed. The master is never affected.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MediaCompressionPreset>))]
public enum MediaCompressionPreset
{
    Balanced = 0,

    HighQuality = 10,

    Smallest = 20,
}

/// <summary>The image types an upload may be (media.md: JPEG, PNG, WebP; never SVG).</summary>
public enum MediaImageFormat
{
    Jpeg = 0,

    Png = 10,

    Webp = 20,
}

public static class MediaImageFormats
{
    public static string MimeType(this MediaImageFormat format) => format switch
    {
        MediaImageFormat.Jpeg => "image/jpeg",
        MediaImageFormat.Png => "image/png",
        MediaImageFormat.Webp => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static string Extension(this MediaImageFormat format) => format switch
    {
        MediaImageFormat.Jpeg => "jpg",
        MediaImageFormat.Png => "png",
        MediaImageFormat.Webp => "webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Whether a file name's extension is one this format is ever saved with.</summary>
    public static bool MatchesExtension(this MediaImageFormat format, string extension) =>
        (format, extension.TrimStart('.').ToUpperInvariant()) switch
        {
            (MediaImageFormat.Jpeg, "JPG" or "JPEG" or "JPE" or "JFIF") => true,
            (MediaImageFormat.Png, "PNG") => true,
            (MediaImageFormat.Webp, "WEBP") => true,
            _ => false,
        };
}
