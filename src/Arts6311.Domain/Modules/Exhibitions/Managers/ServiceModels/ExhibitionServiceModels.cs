using System.Text.Json.Serialization;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;

/// <summary>One exhibition in full, as the CMS edits it.</summary>
/// <param name="StartDate">The first day it hangs, as a calendar date in <paramref name="DisplayTimeZone"/>.</param>
/// <param name="EndDate">The last day it hangs, inclusive.</param>
/// <param name="Reception">The opening reception, when there is one.</param>
/// <param name="DisplayTimeZone">The IANA zone the reception's UTC times are shown in (api-contract.md).</param>
/// <param name="HeroMediaId">The hero's media asset. The admin reads its thumbnail and alt text from Media.</param>
/// <param name="Artists">The credits, in order, as the Artists module describes each artist now.</param>
/// <param name="PublishedAt">When it was first published; kept through an unpublish.</param>
/// <param name="SlugLocked">Whether the exhibition has ever been published, so that changing its slug redirects the old address to the new one (ADR 0035). The name predates 2.7, when such a slug could not change at all.</param>
/// <param name="LegacyWordPressId">The WordPress post it was imported from, for provenance.</param>
/// <param name="UpdatedBy">The user id behind the last write.</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
public sealed record ExhibitionServiceModel(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string? Body,
    DateOnly StartDate,
    DateOnly EndDate,
    ExhibitionReceptionServiceModel? Reception,
    string DisplayTimeZone,
    Guid? HeroMediaId,
    IReadOnlyList<ExhibitionArtistServiceModel> Artists,
    PublishStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledFor,
    bool SlugLocked,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>An opening reception. Both times UTC, shown in the exhibition's <c>displayTimeZone</c>.</summary>
public sealed record ExhibitionReceptionServiceModel(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Note);

/// <summary>One credited artist, as the Artists module describes them now.</summary>
/// <param name="Status">The artist's own status: a credit to an archived or draft artist is shown as such.</param>
public sealed record ExhibitionArtistServiceModel(Guid Id, string Name, string Slug, PublishStatus Status);

/// <summary>One row of the exhibitions list.</summary>
public sealed record ExhibitionSummaryServiceModel(
    Guid Id,
    string Title,
    string Slug,
    DateOnly StartDate,
    DateOnly EndDate,
    PublishStatus Status,
    Guid? HeroMediaId,
    int ArtistCount,
    DateTimeOffset UpdatedAt);
