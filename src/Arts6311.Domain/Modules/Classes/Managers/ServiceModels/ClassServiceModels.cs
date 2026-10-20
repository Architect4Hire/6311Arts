using System.Text.Json.Serialization;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Classes.Managers.ServiceModels;

/// <summary>One class offering in full, as the CMS edits it.</summary>
/// <param name="Body">Sanitized HTML (ADR 0034): exactly what is stored, which is also all the site will render.</param>
/// <param name="StartingPriceCents">The CMS's draft price, the value the first Square push creates the variations with.</param>
/// <param name="Currency">The currency every amount here is in (api-contract.md).</param>
/// <param name="StartingPriceEditable">
/// Whether <paramref name="StartingPriceCents"/> can still be edited: <c>false</c> once Square has reported money or
/// seats for any session, after which a price change is its own command (ADR 0032, 0037).
/// </param>
/// <param name="OfferingNumber">The number its sessions' suggested SKUs are built from (ADR 0037).</param>
/// <param name="ImageMediaIds">The library images the body shows. The admin reads their alt text from Media.</param>
/// <param name="Sessions">The sittings, in start order.</param>
/// <param name="PublishedAt">When it was first published; kept through an unpublish.</param>
/// <param name="SlugLocked">Whether it has ever been published, so that changing its slug redirects the old address to the new one (ADR 0035).</param>
/// <param name="LegacyWordPressId">The WordPress event it was imported from, for provenance.</param>
/// <param name="UpdatedBy">The user id behind the last write.</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
public sealed record ClassServiceModel(
    Guid Id,
    string Title,
    string Slug,
    string? Summary,
    string? Body,
    string? ClassType,
    string? Level,
    string? Instructor,
    string? Cadence,
    long? StartingPriceCents,
    string Currency,
    bool StartingPriceEditable,
    int OfferingNumber,
    Guid? HeroMediaId,
    IReadOnlyList<Guid> ImageMediaIds,
    IReadOnlyList<ClassSessionServiceModel> Sessions,
    PublishStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? ScheduledFor,
    bool SlugLocked,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>
/// One sitting. Its times are UTC, shown in <paramref name="DisplayTimeZone"/> (api-contract.md "Times and money").
/// </summary>
/// <param name="Sku">CMS-owned, unique across the CMS (ADR 0037).</param>
/// <param name="Capacity">CMS-owned: how many seats it has.</param>
/// <param name="PriceCents">Square's record of the price. <c>null</c> until Square has reported one.</param>
/// <param name="SeatsRemaining">Square's record of the seats left. <c>null</c> until the first pull.</param>
/// <param name="SoldOut">Square's record of whether it is sold out.</param>
/// <param name="SquareHasRecord">
/// Whether Square has reported money or seats for this session yet. While it is <c>false</c> the session is still a
/// draft the first push will create; once it is <c>true</c> the session can no longer be removed in the CMS, and
/// price changes are commands (ADR 0032, 0037).
/// </param>
public sealed record ClassSessionServiceModel(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string DisplayTimeZone,
    string Sku,
    int Capacity,
    long? PriceCents,
    int? SeatsRemaining,
    bool SoldOut,
    bool SquareHasRecord);

/// <summary>One row of the class offerings list.</summary>
/// <param name="NextSessionStartsAt">When it next meets, of the sessions still to come; <c>null</c> when none are.</param>
public sealed record ClassSummaryServiceModel(
    Guid Id,
    string Title,
    string Slug,
    PublishStatus Status,
    string? ClassType,
    string? Level,
    string? Instructor,
    Guid? HeroMediaId,
    int SessionCount,
    DateTimeOffset? NextSessionStartsAt,
    DateTimeOffset UpdatedAt);
