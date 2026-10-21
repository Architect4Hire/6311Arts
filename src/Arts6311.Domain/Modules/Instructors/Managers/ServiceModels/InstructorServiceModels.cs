using System.Text.Json.Serialization;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;

/// <summary>One instructor in full, as the CMS edits it.</summary>
/// <param name="PortraitMediaId">The portrait's media asset. The admin reads its thumbnail and alt text from Media.</param>
/// <param name="Artist">The artist they also are, as the Artists module describes them now; <c>null</c> when not linked.</param>
/// <param name="PublishedAt">When it was first published; kept through an unpublish.</param>
/// <param name="SlugLocked">Whether the instructor has ever been published, so that changing its slug redirects the old address to the new one (ADR 0035).</param>
/// <param name="LegacyWordPressId">The WordPress record it was imported from, for provenance.</param>
/// <param name="UpdatedBy">The user id behind the last write.</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
public sealed record InstructorServiceModel(
    Guid Id,
    string Name,
    string Slug,
    string? Medium,
    string? Bio,
    bool Active,
    Guid? PortraitMediaId,
    string? Website,
    string? Instagram,
    InstructorArtistServiceModel? Artist,
    PublishStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledFor,
    bool SlugLocked,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>The artist an instructor is linked to, as the Artists module describes them now.</summary>
public sealed record InstructorArtistServiceModel(Guid Id, string Name, string Slug, PublishStatus Status);

/// <summary>One row of the instructors list, and what another module (Classes) is told about an instructor.</summary>
public sealed record InstructorSummaryServiceModel(
    Guid Id,
    string Name,
    string Slug,
    string? Medium,
    bool Active,
    PublishStatus Status,
    Guid? PortraitMediaId,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One published instructor as the public site shows them (ADR 0042, 0043): no status, no staff bookkeeping, no
/// legacy id.
/// </summary>
/// <param name="Active">Whether they currently teach here, so the directory can group them.</param>
/// <param name="Portrait">The portrait's renditions, or <c>null</c> when there is none yet.</param>
/// <param name="Bio">Plain text; blank lines separate paragraphs. Not HTML.</param>
/// <param name="ArtistSlug">Their artist page, when they are linked to a published artist; otherwise <c>null</c>.</param>
public sealed record SiteInstructorServiceModel(
    Guid Id,
    string Slug,
    string Name,
    string? Medium,
    bool Active,
    SiteImageServiceModel? Portrait,
    string? Bio,
    string? Website,
    string? Instagram,
    string? ArtistSlug);

/// <summary>
/// The instructor a record the site shows names (a class run, ADR 0044).
/// </summary>
/// <param name="Slug">Their page, or <c>null</c> when they are not published, so the site shows the name without a link.</param>
public sealed record SiteInstructorCreditServiceModel(string Name, string? Slug);
