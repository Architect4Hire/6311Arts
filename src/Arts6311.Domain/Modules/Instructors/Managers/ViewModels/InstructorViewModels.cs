using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Instructors.Managers.ViewModels;

/// <summary>A new instructor. It starts as a Draft; publishing is its own command.</summary>
/// <param name="Slug">Optional: made from <paramref name="Name"/> when left out.</param>
/// <param name="PortraitMediaId">An original in the media library. It may lack alt text until the instructor is published.</param>
/// <param name="Website">An absolute <c>https:</c> address.</param>
/// <param name="Instagram">The handle; a leading <c>@</c> is dropped.</param>
/// <param name="ArtistId">The artist on the roster this instructor also is. Not an archived one.</param>
public sealed record InstructorCreateViewModel(
    string Name,
    string? Slug = null,
    string? Medium = null,
    string? Bio = null,
    bool Active = true,
    Guid? PortraitMediaId = null,
    string? Website = null,
    string? Instagram = null,
    Guid? ArtistId = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged; an empty string clears <c>medium</c>, <c>bio</c>,
/// <c>website</c> or <c>instagram</c>. Status is not here: it changes only through the publish, unpublish, schedule
/// and archive commands (content.md).
/// </summary>
/// <param name="Slug">Once the instructor has been published, changing it redirects the old address to the new one (ADR 0035).</param>
/// <param name="RemovePortrait"><c>true</c> takes the portrait off; not allowed together with <paramref name="PortraitMediaId"/>.</param>
/// <param name="RemoveArtist"><c>true</c> takes the artist link off; not allowed together with <paramref name="ArtistId"/>.</param>
public sealed record InstructorUpdateViewModel(
    string? Name = null,
    string? Slug = null,
    string? Medium = null,
    string? Bio = null,
    bool? Active = null,
    Guid? PortraitMediaId = null,
    bool? RemovePortrait = null,
    string? Website = null,
    string? Instagram = null,
    Guid? ArtistId = null,
    bool? RemoveArtist = null);

/// <summary>A page of instructors, by name.</summary>
/// <param name="Search">Part of a name, matched without regard to case.</param>
/// <param name="Sort">
/// name, medium, status, teaching, updated — with a leading <c>-</c> for descending (ADR 0062). The Deleted
/// view ignores it.
/// </param>
public sealed record InstructorListViewModel(
    string? Cursor = null,
    int? Limit = null,
    PublishStatus? Status = null,
    string? Search = null,
    string? Sort = null);
