namespace Arts6311.Domain.Modules.Media.Managers.ViewModels;

/// <summary>
/// One upload, as the controller reads it from the multipart request. <see cref="Content"/> is the file part's
/// body, still streaming: nothing has buffered it, and nothing will hold all of it in memory.
/// </summary>
/// <param name="FileName">The name the browser sent, for display only.</param>
/// <param name="FromSubmission">
/// An artist's upload through the public form (ADR 0047): stored as <see cref="MediaAssetKind.Submission"/> under the
/// smaller <see cref="MediaPolicy.MaxSubmissionImageBytes"/>, and a picture already held is linked, not refused.
/// </param>
/// <param name="SiteImageId">
/// One of the site's own pictures (<see cref="MediaSiteImages"/>, prompt 3.3): stored at this fixed id and published
/// at once, because the site's pages name it by that id in every environment. Set only by MigrationService's seed.
/// </param>
public sealed record MediaUploadViewModel(
    string FileName,
    Stream Content,
    string? AltText,
    string? Credit,
    MediaCompressionPreset? CompressionPreset,
    bool FromSubmission = false,
    Guid? SiteImageId = null);

/// <summary>A page of the library: originals only, newest first.</summary>
/// <param name="MissingAltText">Only assets that still need alt text before they can go on published content.</param>
public sealed record MediaListViewModel(
    string? Cursor = null,
    int? Limit = null,
    bool? MissingAltText = null,
    MediaProcessingStatus? Status = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged; an empty string clears it. The bytes, dimensions
/// and visibility are not editable here: an original is immutable, and visibility follows content publishing.
/// </summary>
public sealed record MediaAssetUpdateViewModel(
    string? AltText = null,
    string? Credit = null,
    MediaCompressionPreset? CompressionPreset = null);

/// <summary>A request for an asset's bytes on <c>/media/{id}/{rendition}</c>.</summary>
/// <param name="Version">The URL's <c>?v=</c>, if any.</param>
/// <param name="IfNoneMatch">The request's <c>If-None-Match</c>, answered with a 304 when it matches.</param>
/// <param name="HeadOnly">A <c>HEAD</c>: everything but the body, so no blob is opened.</param>
public sealed record MediaDeliveryViewModel(
    Guid AssetId,
    string Rendition,
    string? Version,
    string? IfNoneMatch,
    bool HeadOnly);

/// <summary>Who is asking for an asset. Drafts are served to CMS sessions only (media.md).</summary>
public enum MediaViewer
{
    Public = 0,

    /// <summary>A signed-in Admin or Editor.</summary>
    Staff = 10,
}
