using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;

/// <summary>A new exhibition. It starts as a Draft; publishing is its own command.</summary>
/// <param name="StartDate">Required: the first day it hangs.</param>
/// <param name="EndDate">Required: the last day it hangs, on or after <paramref name="StartDate"/>.</param>
/// <param name="Slug">Optional: made from <paramref name="Title"/> when left out.</param>
/// <param name="Summary">Optional on a draft; required to publish.</param>
/// <param name="HeroMediaId">An original in the media library. It may lack alt text until the exhibition is published.</param>
/// <param name="ArtistIds">The credits, in order. Each an artist on the roster that is not archived.</param>
public sealed record ExhibitionCreateViewModel(
    string Title,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Slug = null,
    string? Summary = null,
    string? Body = null,
    ExhibitionReceptionViewModel? Reception = null,
    Guid? HeroMediaId = null,
    IReadOnlyList<Guid>? ArtistIds = null);

/// <summary>An opening reception: both times, with any offset (they are kept as UTC), and an optional note.</summary>
public sealed record ExhibitionReceptionViewModel(DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Note = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged; an empty string clears <c>summary</c> or <c>body</c>.
/// Status is not here: it changes only through the publish, unpublish and archive commands (content.md).
/// </summary>
/// <param name="Slug">Once the exhibition has been published, changing it redirects the old address to the new one (ADR 0035).</param>
/// <param name="Reception">Replaces the reception, note included. Not allowed with <paramref name="RemoveReception"/>.</param>
/// <param name="RemoveReception"><c>true</c> takes the reception off.</param>
/// <param name="RemoveHero"><c>true</c> takes the hero off; not allowed together with <paramref name="HeroMediaId"/>.</param>
/// <param name="ArtistIds">Replaces the credits, in order; an empty list clears them.</param>
public sealed record ExhibitionUpdateViewModel(
    string? Title = null,
    string? Slug = null,
    string? Summary = null,
    string? Body = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    ExhibitionReceptionViewModel? Reception = null,
    bool? RemoveReception = null,
    Guid? HeroMediaId = null,
    bool? RemoveHero = null,
    IReadOnlyList<Guid>? ArtistIds = null);

/// <summary>A page of exhibitions, newest show first.</summary>
/// <param name="Search">Part of a title, matched without regard to case.</param>
public sealed record ExhibitionListViewModel(
    string? Cursor = null,
    int? Limit = null,
    PublishStatus? Status = null,
    string? Search = null);
