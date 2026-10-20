using System.Text.Json.Serialization;
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
