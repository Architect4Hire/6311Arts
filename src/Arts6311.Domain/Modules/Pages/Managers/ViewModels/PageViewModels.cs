using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Pages.Managers.ViewModels;

/// <summary>A new page. It starts as a Draft; publishing is its own command.</summary>
/// <param name="Slug">Optional: made from <paramref name="Title"/> when left out.</param>
/// <param name="Body">HTML. It is sanitized before it is stored (ADR 0034); what comes back is what was kept.</param>
public sealed record PageCreateViewModel(string Title, string? Slug = null, string? Body = null);

/// <summary>
/// A staff edit. A field left out (or <c>null</c>) is unchanged; an empty string clears the body. Status is not
/// here: it changes only through the publish, unpublish and archive commands (content.md).
/// </summary>
/// <param name="Slug">Once the page has been published, changing it redirects the old address to the new one (ADR 0035).</param>
public sealed record PageUpdateViewModel(string? Title = null, string? Slug = null, string? Body = null);

/// <summary>A page of pages, by title.</summary>
/// <param name="Search">Part of a title, matched without regard to case.</param>
/// <param name="Sort">
/// title, status, updated — with a leading <c>-</c> for descending (ADR 0062). The Deleted
/// view ignores it.
/// </param>
public sealed record PageListViewModel(
    string? Cursor = null,
    int? Limit = null,
    PublishStatus? Status = null,
    string? Search = null,
    string? Sort = null);
