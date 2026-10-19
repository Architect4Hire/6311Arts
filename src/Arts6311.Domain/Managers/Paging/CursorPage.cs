namespace Arts6311.Domain.Managers.Paging;

/// <summary>
/// One page of a cursor-paginated collection: the items, and where to resume. <see cref="NextCursor"/> is
/// <c>null</c> on the last page, so a client loops until it is null instead of comparing counts.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>A request for one page: an opaque cursor (absent for the first page) and a requested size.</summary>
public sealed record PageRequest(string? Cursor, int? Limit)
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    /// <summary>The page size to use: the request's, clamped to 1..<see cref="MaxLimit"/>, or the default.</summary>
    public int EffectiveLimit => Limit is { } limit ? Math.Clamp(limit, 1, MaxLimit) : DefaultLimit;
}
