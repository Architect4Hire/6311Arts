using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Classes.Managers.ViewModels;

/// <summary>
/// A new class offering. It starts as a Draft; publishing is its own command.
/// </summary>
/// <remarks>
/// No price, seat count or sold-out field appears on this model or any other write model in this module: those are
/// Square's record, kept as read-only projections (square.md, ADR 0032). <c>StartingPriceCents</c> is the CMS's own
/// draft field, the value the first Square push creates the variations with.
/// </remarks>
/// <param name="Slug">Optional: made from <paramref name="Title"/> when left out.</param>
/// <param name="Summary">Optional on a draft; required to publish.</param>
/// <param name="Body">HTML. It is sanitized before it is stored (ADR 0034); what comes back is what was kept.</param>
/// <param name="StartingPriceCents">What one seat costs when Square's objects are created. Editable until Square reports a price (ADR 0037).</param>
/// <param name="HeroMediaId">An original in the media library. It may lack alt text until the offering is published.</param>
/// <param name="Sessions">The sittings, in any order. Each gets a suggested SKU when none is sent.</param>
public sealed record ClassCreateViewModel(
    string Title,
    string? Slug = null,
    string? Summary = null,
    string? Body = null,
    string? ClassType = null,
    string? Level = null,
    string? Instructor = null,
    string? Cadence = null,
    long? StartingPriceCents = null,
    Guid? HeroMediaId = null,
    IReadOnlyList<ClassSessionViewModel>? Sessions = null);

/// <summary>
/// One sitting, as staff write it. Capacity and SKU are the CMS's (ADR 0032); the seats left and the price are not
/// here, because they are Square's record.
/// </summary>
/// <param name="StartsAt">Required. Any offset; it is kept as UTC.</param>
/// <param name="EndsAt">Required, after <paramref name="StartsAt"/>.</param>
/// <param name="Capacity">Required: how many seats it has.</param>
/// <remarks>
/// <strong>The three required fields are nullable types, and the validator refuses <c>null</c>.</strong> That
/// reads oddly against the schema, which lists them as required and nullable, so it is deliberate: a
/// non-nullable <c>DateTimeOffset</c> binds a missing <c>startsAt</c> to <c>0001-01-01</c> and a missing
/// <c>capacity</c> to <c>0</c>, which the validator could only refuse as "out of range" — indistinguishable
/// from a client that really sent those values. Keeping them nullable is what lets an omitted field answer
/// "Every session needs a start and an end time." Sending <c>null</c> explicitly is a
/// <c>validation.failed</c>, never a 500 (api-contract review).
/// </remarks>
/// <param name="Id">The session being kept, on an edit. Left out for a new one.</param>
/// <param name="Sku">Optional: the next free suggestion (<c>CLS-&lt;offering&gt;-&lt;n&gt;</c>) when left out.</param>
/// <param name="DisplayTimeZone">The IANA zone its times are shown in. The gallery's zone when left out.</param>
public sealed record ClassSessionViewModel(
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int? Capacity,
    Guid? Id = null,
    string? Sku = null,
    string? DisplayTimeZone = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged; an empty string clears a text field. Status is not
/// here: it changes only through the publish, unpublish, schedule and archive commands (content.md).
/// </summary>
/// <param name="Slug">Once the offering has been published, changing it redirects the old address to the new one (ADR 0035).</param>
/// <param name="StartingPriceCents">Refused once Square has reported a price: from then on it is a change-price command (5.4a).</param>
/// <param name="RemoveStartingPrice"><c>true</c> clears it; not allowed together with <paramref name="StartingPriceCents"/>.</param>
/// <param name="RemoveHero"><c>true</c> takes the hero off; not allowed together with <paramref name="HeroMediaId"/>.</param>
/// <param name="Sessions">Replaces the sittings: one entry per session to keep or add, an empty list clears them.</param>
public sealed record ClassUpdateViewModel(
    string? Title = null,
    string? Slug = null,
    string? Summary = null,
    string? Body = null,
    string? ClassType = null,
    string? Level = null,
    string? Instructor = null,
    string? Cadence = null,
    long? StartingPriceCents = null,
    bool? RemoveStartingPrice = null,
    Guid? HeroMediaId = null,
    bool? RemoveHero = null,
    IReadOnlyList<ClassSessionViewModel>? Sessions = null);

/// <summary>A page of class offerings, by title.</summary>
/// <param name="Search">Part of a title, matched without regard to case.</param>
public sealed record ClassListViewModel(
    string? Cursor = null,
    int? Limit = null,
    PublishStatus? Status = null,
    string? Search = null);
