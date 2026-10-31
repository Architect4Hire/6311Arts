using Arts6311.Domain.Modules.Redirects.Managers;

namespace Arts6311.Domain.Modules.Redirects.Managers.ViewModels;

/// <summary>A new redirect, written by staff (<see cref="RedirectSource.Manual"/>).</summary>
/// <param name="FromPath">The old address on this site, starting with <c>/</c>. Normalised: lower case, no trailing slash.</param>
/// <param name="ToPath">A site path, or a full <c>https://</c> address.</param>
/// <param name="StatusCode">301 (the default) or 302.</param>
public sealed record RedirectCreateViewModel(string FromPath, string ToPath, int? StatusCode = null);

/// <summary>An edit. A field left out (or <c>null</c>) is unchanged.</summary>
public sealed record RedirectUpdateViewModel(string? FromPath = null, string? ToPath = null, int? StatusCode = null);

/// <summary>A page of redirects, by old address unless <paramref name="Sort"/> says otherwise.</summary>
/// <param name="Search">Part of either address, matched without regard to case.</param>
/// <param name="Sort">
/// <c>from</c>, <c>to</c>, <c>type</c> or <c>source</c>, with a leading <c>-</c> for descending (ADR 0062).
/// </param>
public sealed record RedirectListViewModel(
    string? Cursor = null,
    int? Limit = null,
    RedirectSource? Source = null,
    string? Search = null,
    string? Sort = null);

/// <summary>A slug change the Worker applies (<c>redirects.slug-changed</c>, ADR 0035).</summary>
/// <param name="OccurredAt">When the rename was saved, so renames applied out of order never undo a later one.</param>
public sealed record RedirectSlugChangeViewModel(
    string ContentType, Guid ContentId, string FromPath, string ToPath, DateTimeOffset OccurredAt);
