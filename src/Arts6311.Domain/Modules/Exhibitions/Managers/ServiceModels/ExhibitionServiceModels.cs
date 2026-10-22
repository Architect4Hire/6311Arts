using System.Text.Json.Serialization;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
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

/// <summary>
/// One published exhibition in the site's list (ADR 0042): no body, status or staff bookkeeping.
/// </summary>
/// <param name="StartDate">The first day it hangs, as a calendar date in <paramref name="DisplayTimeZone"/>.</param>
/// <param name="EndDate">The last day it hangs, inclusive.</param>
/// <param name="Artists">The credited artists, in the order staff set.</param>
/// <param name="Image">The hero's renditions, or <c>null</c> when there is none yet.</param>
public sealed record SiteExhibitionSummaryServiceModel(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    DateOnly StartDate,
    DateOnly EndDate,
    ExhibitionReceptionServiceModel? Reception,
    string DisplayTimeZone,
    IReadOnlyList<SiteArtistCreditServiceModel> Artists,
    SiteImageServiceModel? Image);

/// <summary>One published exhibition's page (ADR 0042): the list's fields and the body.</summary>
/// <param name="BodyText">Plain text; blank lines separate paragraphs. Not HTML: a sanitized body would be a new
/// <c>bodyHtml</c> field, as classes and pages have.</param>
public sealed record SiteExhibitionServiceModel(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string? BodyText,
    DateOnly StartDate,
    DateOnly EndDate,
    ExhibitionReceptionServiceModel? Reception,
    string DisplayTimeZone,
    IReadOnlyList<SiteArtistCreditServiceModel> Artists,
    SiteImageServiceModel? Image);

/// <summary>One row of the Deleted view (ADR 0052). Admin only; restoring it brings the exhibition back as a Draft.</summary>
/// <param name="Status">What it was when it was deleted — Draft, Published, Archived — not where it is now.</param>
/// <param name="DeletedAt">When it was deleted (UTC).</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c> when restoring it.</param>
public sealed record ExhibitionDeletedServiceModel(
    Guid Id,
    string Title,
    string Slug,
    PublishStatus Status,
    DateTimeOffset DeletedAt,
    [property: JsonPropertyName("etag")] string ETag);
