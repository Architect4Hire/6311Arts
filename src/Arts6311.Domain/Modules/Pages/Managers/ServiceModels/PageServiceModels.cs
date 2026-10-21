using System.Text.Json.Serialization;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Pages.Managers.ServiceModels;

/// <summary>One page in full, as the CMS edits it.</summary>
/// <param name="Body">Sanitized HTML (ADR 0034): exactly what is stored, which is also all the site will render.</param>
/// <param name="ImageMediaIds">The library images the body shows. The admin reads their alt text from Media.</param>
/// <param name="PublishedAt">When it was first published; kept through an unpublish.</param>
/// <param name="SlugLocked">Whether the page has ever been published, so that changing its slug redirects the old address to the new one (ADR 0035). The name predates 2.7, when such a slug could not change at all.</param>
/// <param name="LegacyWordPressId">The WordPress page it was imported from, for provenance.</param>
/// <param name="UpdatedBy">The user id behind the last write.</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
public sealed record PageServiceModel(
    Guid Id,
    string Title,
    string Slug,
    string? Body,
    IReadOnlyList<Guid> ImageMediaIds,
    PublishStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledFor,
    bool SlugLocked,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>One row of the pages list.</summary>
public sealed record PageSummaryServiceModel(
    Guid Id,
    string Title,
    string Slug,
    PublishStatus Status,
    DateTimeOffset UpdatedAt);

/// <summary>One published page as the public site shows it (ADR 0042): no status, no staff bookkeeping.</summary>
/// <param name="BodyHtml">Sanitized HTML (ADR 0034): exactly what is stored, which is all the site renders.</param>
/// <param name="UpdatedAt">When it last changed (UTC), for a "last updated" line on a policy.</param>
public sealed record SitePageServiceModel(Guid Id, string Slug, string Title, string? BodyHtml, DateTimeOffset UpdatedAt);
