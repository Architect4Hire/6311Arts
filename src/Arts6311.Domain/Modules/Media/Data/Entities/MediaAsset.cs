using Arts6311.Domain.Modules.Media.Managers;

namespace Arts6311.Domain.Modules.Media.Data.Entities;

/// <summary>
/// One image in blob storage: an original (the compressed master of a staff upload) or one of its renditions
/// (media.md). The bytes are in the <c>media</c> container at <see cref="BlobKey"/>; this row is everything else.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Content that uses an image references the original by a foreign key to <see cref="Id"/> with
/// <c>DeleteBehavior.Restrict</c></strong> — an artist's portrait, an exhibition's hero, later a
/// <c>SquareCatalogLink</c>. That foreign key is how "delete is blocked while referenced" knows about modules this
/// one never names: the repository finds every such key in the EF model (ADR 0030).
/// </para>
/// <para>No Square id, ever: the Square image id lives on <c>SquareCatalogLink</c> (media.md, ADR 0006).</para>
/// </remarks>
public sealed class MediaAsset
{
    public Guid Id { get; set; }

    public MediaAssetKind Kind { get; set; }

    /// <summary>The original a rendition was made from; <c>null</c> on an original.</summary>
    public Guid? ParentAssetId { get; set; }

    /// <summary>The rendition token, for example <c>800.webp</c>; <c>null</c> on an original.</summary>
    public string? RenditionName { get; set; }

    /// <summary>The name the file was uploaded with, for staff to recognise it. Never used as alt text (media.md).</summary>
    public string? FileName { get; set; }

    /// <summary>From the file signature, never the extension or the browser's header.</summary>
    public string MimeType { get; set; } = string.Empty;

    /// <summary>The stored bytes: the compressed master for an original.</summary>
    public long Bytes { get; set; }

    /// <summary>What was uploaded, before compression. Originals only; shown as the saving.</summary>
    public long? UploadedBytes { get; set; }

    /// <summary>As displayed: the EXIF orientation already accounted for.</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>SHA-256 of the stored bytes, lower-case hex. Also the strong ETag.</summary>
    public string Checksum { get; set; } = string.Empty;

    public string BlobKey { get; set; } = string.Empty;

    /// <summary>Renditions only: which encoder settings produced it (see <c>RenditionProfile</c>).</summary>
    public string? ProfileHash { get; set; }

    public MediaProcessingStatus ProcessingStatus { get; set; }

    public MediaVisibility Visibility { get; set; }

    public MediaCompressionPreset CompressionPreset { get; set; }

    /// <summary>What is in the picture. Required before an asset may be attached to published content.</summary>
    public string? AltText { get; set; }

    public string? Credit { get; set; }

    public string UploadedBy { get; set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; set; }

    public string? UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// The optimistic-concurrency token, rotated by every staff edit and carried as the <c>ETag</c>
    /// (api-contract.md "Concurrency"). A <see cref="Guid"/> the application sets rather than a SQL
    /// <c>rowversion</c>, so the same rule holds on the SQLite test database. The Worker's status updates leave it
    /// alone, so finishing renditions never invalidates an editor's open form.
    /// </summary>
    public Guid RowVersion { get; set; }
}
