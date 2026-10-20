using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Artists.Managers.ViewModels;

/// <summary>A new artist. It starts as a Draft; publishing is its own command.</summary>
/// <param name="Slug">Optional: made from <paramref name="Name"/> when left out.</param>
/// <param name="PortraitMediaId">An original in the media library. It may lack alt text until the artist is published.</param>
public sealed record ArtistCreateViewModel(
    string Name,
    string? Slug = null,
    string? Medium = null,
    string? Bio = null,
    bool Active = true,
    Guid? PortraitMediaId = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged; an empty string clears <c>medium</c> or <c>bio</c>.
/// Status is not here: it changes only through the publish, unpublish and archive commands (content.md).
/// </summary>
/// <param name="Slug">Refused with <c>cms.slug_locked</c> once the artist has been published.</param>
/// <param name="RemovePortrait"><c>true</c> takes the portrait off; not allowed together with <paramref name="PortraitMediaId"/>.</param>
public sealed record ArtistUpdateViewModel(
    string? Name = null,
    string? Slug = null,
    string? Medium = null,
    string? Bio = null,
    bool? Active = null,
    Guid? PortraitMediaId = null,
    bool? RemovePortrait = null);

/// <summary>A page of the roster, by name.</summary>
/// <param name="Search">Part of a name, matched without regard to case.</param>
public sealed record ArtistListViewModel(
    string? Cursor = null,
    int? Limit = null,
    PublishStatus? Status = null,
    string? Search = null);
