using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Classes.Managers.ViewModels;

/// <summary>
/// A new class offering. It starts as a Draft; publishing is its own command.
/// </summary>
/// <remarks>
/// No price, seat count or sold-out field appears on this model or any other write model in this module: those are
/// Square's record, kept as read-only projections on each run (square.md, ADR 0032). A run's
/// <c>StartingPriceCents</c> is the CMS's own draft field, the value the first Square push creates its variation with.
/// </remarks>
/// <param name="Slug">Optional: made from <paramref name="Title"/> when left out.</param>
/// <param name="Summary">Optional on a draft; required to publish.</param>
/// <param name="Body">HTML. It is sanitized before it is stored (ADR 0034); what comes back is what was kept.</param>
/// <param name="HeroMediaId">An original in the media library. It may lack alt text until the offering is published.</param>
/// <param name="Runs">The terms it is offered in, each with its instructor and sittings (ADR 0044).</param>
public sealed record ClassCreateViewModel(
    string Title,
    string? Slug = null,
    string? Summary = null,
    string? Body = null,
    string? ClassType = null,
    string? Level = null,
    Guid? HeroMediaId = null,
    IReadOnlyList<ClassRunViewModel>? Runs = null);

/// <summary>
/// One run, as staff write it: who teaches it, when it meets, and what is sold. Capacity and SKU are the CMS's (ADR
/// 0032); the seats left and the price are not here, because they are Square's record.
/// </summary>
/// <remarks>
/// <strong><paramref name="Capacity"/> is a nullable type the validator refuses as <c>null</c></strong>, as a
/// session's times are: a non-nullable <c>int</c> binds a missing <c>capacity</c> to <c>0</c>, which the validator
/// could only call "out of range". Keeping it nullable is what lets an omitted field answer "Every run needs a number
/// of seats." (api-contract review).
/// </remarks>
/// <param name="Capacity">Required: how many seats it has.</param>
/// <param name="Id">The run being kept, on an edit. Left out for a new one.</param>
/// <param name="Label">What it is called when the class has several: "Fall 2026".</param>
/// <param name="InstructorId">An instructor in the Instructors module. Not an archived one, unless the run already had them. Required to publish.</param>
/// <param name="Sku">Optional: the next free suggestion (<c>CLS-&lt;offering&gt;-&lt;n&gt;</c>) when left out.</param>
/// <param name="DisplayTimeZone">The IANA zone its times are shown in. The gallery's zone when left out.</param>
/// <param name="StartingPriceCents">
/// What one seat costs when Square's variation is created. <c>null</c> clears it while it is still editable; once
/// Square has reported a price it may only be left out or sent unchanged (ADR 0037).
/// </param>
/// <param name="Sessions">The sittings, in any order; they replace the run's.</param>
public sealed record ClassRunViewModel(
    int? Capacity,
    Guid? Id = null,
    string? Label = null,
    Guid? InstructorId = null,
    string? Cadence = null,
    string? Sku = null,
    string? DisplayTimeZone = null,
    long? StartingPriceCents = null,
    IReadOnlyList<ClassSessionViewModel>? Sessions = null);

/// <summary>One sitting of a run, as staff write it: when it meets.</summary>
/// <param name="StartsAt">Required. Any offset; it is kept as UTC.</param>
/// <param name="EndsAt">Required, after <paramref name="StartsAt"/>.</param>
/// <remarks>
/// <strong>Both times are nullable types, and the validator refuses <c>null</c>.</strong> A non-nullable
/// <c>DateTimeOffset</c> binds a missing <c>startsAt</c> to <c>0001-01-01</c>, indistinguishable from a client that
/// really sent it. Keeping them nullable is what lets an omitted field answer "Every session needs a start and an end
/// time." Sending <c>null</c> explicitly is a <c>validation.failed</c>, never a 500 (api-contract review).
/// </remarks>
/// <param name="Id">The session being kept, on an edit. Left out for a new one.</param>
public sealed record ClassSessionViewModel(
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    Guid? Id = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged; an empty string clears a text field. Status is not
/// here: it changes only through the publish, unpublish, schedule and archive commands (content.md).
/// </summary>
/// <param name="Slug">Once the offering has been published, changing it redirects the old address to the new one (ADR 0035).</param>
/// <param name="RemoveHero"><c>true</c> takes the hero off; not allowed together with <paramref name="HeroMediaId"/>.</param>
/// <param name="Runs">Replaces the runs: one entry per run to keep or add, an empty list clears them.</param>
public sealed record ClassUpdateViewModel(
    string? Title = null,
    string? Slug = null,
    string? Summary = null,
    string? Body = null,
    string? ClassType = null,
    string? Level = null,
    Guid? HeroMediaId = null,
    bool? RemoveHero = null,
    IReadOnlyList<ClassRunViewModel>? Runs = null);

/// <summary>A page of class offerings, by title.</summary>
/// <param name="Search">Part of a title, matched without regard to case.</param>
/// <param name="Sort">
/// title, status, runs, updated — with a leading <c>-</c> for descending (ADR 0062). The Deleted
/// view ignores it.
/// </param>
public sealed record ClassListViewModel(
    string? Cursor = null,
    int? Limit = null,
    PublishStatus? Status = null,
    string? Search = null,
    string? Sort = null);

/// <summary>The site's class list (<c>GET /api/v1/site/classes</c>).</summary>
/// <param name="Instructor">An instructor's slug: only the runs they teach, for their page (ADR 0044).</param>
public sealed record SiteClassListViewModel(string? Instructor = null);
