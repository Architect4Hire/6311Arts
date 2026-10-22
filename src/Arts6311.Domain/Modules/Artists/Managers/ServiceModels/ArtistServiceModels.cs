using System.Text.Json.Serialization;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Artists.Managers.ServiceModels;

/// <summary>One artist in full, as the CMS edits it.</summary>
/// <param name="PortraitMediaId">The portrait's media asset. The admin reads its thumbnail and alt text from Media.</param>
/// <param name="PublishedAt">When it was first published; kept through an unpublish.</param>
/// <param name="SlugLocked">Whether the artist has ever been published, so that changing its slug redirects the old address to the new one (ADR 0035). The name predates 2.7, when such a slug could not change at all.</param>
/// <param name="LegacyWordPressId">The WordPress term it was imported from, for provenance.</param>
/// <param name="UpdatedBy">The user id behind the last write.</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
public sealed record ArtistServiceModel(
    Guid Id,
    string Name,
    string Slug,
    string? Medium,
    string? Bio,
    bool Active,
    Guid? PortraitMediaId,
    PublishStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledFor,
    bool SlugLocked,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>One row of the roster list.</summary>
public sealed record ArtistSummaryServiceModel(
    Guid Id,
    string Name,
    string Slug,
    string? Medium,
    bool Active,
    PublishStatus Status,
    Guid? PortraitMediaId,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One published artist as the public site shows them (ADR 0042): no status, no staff bookkeeping, no legacy id.
/// </summary>
/// <param name="Active">Whether they currently show at the gallery, so the directory can group them.</param>
/// <param name="Portrait">The portrait's renditions, or <c>null</c> when there is none yet.</param>
/// <param name="BioText">Plain text; blank lines separate paragraphs. Not HTML (ADR 0031).</param>
public sealed record SiteArtistServiceModel(
    Guid Id,
    string Slug,
    string Name,
    string? Medium,
    bool Active,
    SiteImageServiceModel? Portrait,
    string? BioText);

/// <summary>
/// An artist credited on another record the site shows (an exhibition, a piece of artwork).
/// </summary>
/// <param name="Slug">Their page, or <c>null</c> when they are not published, so the site shows the name without a link.</param>
public sealed record SiteArtistCreditServiceModel(string Name, string? Slug);

/// <summary>One row of the Deleted view (ADR 0052). Admin only; restoring it brings the artist back as a Draft.</summary>
/// <param name="Status">What it was when it was deleted — Draft, Published, Archived — not where it is now.</param>
/// <param name="DeletedAt">When it was deleted (UTC).</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c> when restoring it.</param>
public sealed record ArtistDeletedServiceModel(
    Guid Id,
    string Name,
    string Slug,
    PublishStatus Status,
    DateTimeOffset DeletedAt,
    [property: JsonPropertyName("etag")] string ETag);
