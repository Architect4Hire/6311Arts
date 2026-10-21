using System.Text.Json.Serialization;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Classes.Managers.ServiceModels;

/// <summary>One class offering in full, as the CMS edits it.</summary>
/// <param name="Body">Sanitized HTML (ADR 0034): exactly what is stored, which is also all the site will render.</param>
/// <param name="OfferingNumber">The number its runs' suggested SKUs are built from (ADR 0037).</param>
/// <param name="ImageMediaIds">The library images the body shows. The admin reads their alt text from Media.</param>
/// <param name="Runs">The terms it is offered in, by their first session (ADR 0044).</param>
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
    int OfferingNumber,
    Guid? HeroMediaId,
    IReadOnlyList<Guid> ImageMediaIds,
    IReadOnlyList<ClassRunServiceModel> Runs,
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
/// One run (ADR 0044): who teaches it, when it meets, and what Square sells for it. Its times are UTC, shown in
/// <paramref name="DisplayTimeZone"/> (api-contract.md "Times and money").
/// </summary>
/// <param name="Instructor">Who teaches it, as the Instructors module describes them now; <c>null</c> when nobody is set yet.</param>
/// <param name="Sku">CMS-owned, unique across the CMS (ADR 0037).</param>
/// <param name="Capacity">CMS-owned: how many seats it has.</param>
/// <param name="StartingPriceCents">The CMS's draft price, the value the first Square push creates the variation with.</param>
/// <param name="Currency">The currency every amount here is in (api-contract.md).</param>
/// <param name="StartingPriceEditable">
/// Whether <paramref name="StartingPriceCents"/> can still be edited: <c>false</c> once Square has reported money or
/// seats for this run, after which a price change is its own command (ADR 0032, 0037).
/// </param>
/// <param name="PriceCents">Square's record of the price. <c>null</c> until Square has reported one.</param>
/// <param name="SeatsRemaining">Square's record of the seats left. <c>null</c> until the first pull.</param>
/// <param name="SoldOut">Square's record of whether it is sold out.</param>
/// <param name="SquareHasRecord">
/// Whether Square has reported money or seats for this run yet. Once it has, the run can no longer be removed in the
/// CMS, and price changes are commands (ADR 0032, 0037).
/// </param>
/// <param name="Sessions">The sittings, in start order.</param>
public sealed record ClassRunServiceModel(
    Guid Id,
    string? Label,
    ClassInstructorServiceModel? Instructor,
    string? Cadence,
    string DisplayTimeZone,
    string Sku,
    int Capacity,
    long? StartingPriceCents,
    string Currency,
    bool StartingPriceEditable,
    long? PriceCents,
    int? SeatsRemaining,
    bool SoldOut,
    bool SquareHasRecord,
    IReadOnlyList<ClassSessionServiceModel> Sessions);

/// <summary>The instructor a run names, as the Instructors module describes them now.</summary>
public sealed record ClassInstructorServiceModel(Guid Id, string Name, string Slug, PublishStatus Status);

/// <summary>One sitting: when it meets. UTC, shown in its run's display zone.</summary>
public sealed record ClassSessionServiceModel(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>One row of the class offerings list.</summary>
/// <param name="Instructors">Who teaches its runs, by name, each once.</param>
/// <param name="RunCount">How many runs it has, past ones included.</param>
/// <param name="NextSessionStartsAt">When it next meets, of every run's sessions still to come; <c>null</c> when none are.</param>
public sealed record ClassSummaryServiceModel(
    Guid Id,
    string Title,
    string Slug,
    PublishStatus Status,
    string? ClassType,
    string? Level,
    IReadOnlyList<string> Instructors,
    Guid? HeroMediaId,
    int RunCount,
    DateTimeOffset? NextSessionStartsAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One run still to come as the site shows it (ADR 0042, 0044): who teaches it, its schedule, the capacity staff set,
/// and Square's record of the price and seats. No SKU.
/// </summary>
/// <param name="Instructor">Who teaches it; their page's slug only when it is published. <c>null</c> when nobody is set.</param>
/// <param name="StartsAt">When its first session starts. UTC; shown as a wall clock in <paramref name="DisplayTimeZone"/>.</param>
/// <param name="EndsAt">When its last session ends.</param>
/// <param name="SessionCount">How many sittings it has, past ones included: "a five-week course".</param>
/// <param name="PriceCents">Square's record of the price; <c>null</c> until Square has reported one.</param>
/// <param name="SeatsRemaining">Square's record of the seats left; <c>null</c> until the first pull.</param>
/// <param name="Sessions">Every sitting, in start order: the schedule a seat buys.</param>
public sealed record SiteClassRunServiceModel(
    Guid Id,
    string? Label,
    SiteInstructorCreditServiceModel? Instructor,
    string? Cadence,
    string DisplayTimeZone,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int SessionCount,
    int Capacity,
    long? PriceCents,
    string Currency,
    int? SeatsRemaining,
    bool SoldOut,
    IReadOnlyList<ClassSessionServiceModel> Sessions);

/// <summary>
/// One published class in the site's list (ADR 0042, 0044): its copy, its hero, and the runs still to come.
/// </summary>
/// <param name="FromPriceCents">The lowest price Square has reported among the upcoming runs, or <c>null</c>.</param>
/// <param name="Runs">The runs that have not ended yet, soonest first.</param>
public sealed record SiteClassSummaryServiceModel(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string? ClassType,
    string? Level,
    SiteImageServiceModel? Image,
    long? FromPriceCents,
    string Currency,
    IReadOnlyList<SiteClassRunServiceModel> Runs);

/// <summary>One published class's page (ADR 0042, 0044): the list's fields and the body.</summary>
/// <param name="BodyHtml">Sanitized HTML (ADR 0034): exactly what is stored, which is all the site renders.</param>
/// <param name="Runs">The runs that have not ended yet, soonest first; empty once the class is over.</param>
public sealed record SiteClassServiceModel(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string? BodyHtml,
    string? ClassType,
    string? Level,
    SiteImageServiceModel? Image,
    long? FromPriceCents,
    string Currency,
    IReadOnlyList<SiteClassRunServiceModel> Runs);
