namespace Arts6311.Domain.Managers.Paging;

/// <summary>Which way a list runs on its sort column.</summary>
public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>
/// The order a CMS list was asked for: one of the columns that list sorts by, and a direction. On the wire it is
/// the column's name for ascending and the name after a minus for descending — <c>name</c>, <c>-updated</c>
/// (ADR 0062).
/// </summary>
public sealed record ListSort(string Column, SortDirection Direction)
{
    /// <summary>The sort a query string names, or <c>null</c> for none or for one with no column.</summary>
    public static ListSort? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var descending = text[0] == '-';
        var column = descending ? text[1..] : text;
        return column.Length == 0 ? null : new ListSort(column, descending ? SortDirection.Descending : SortDirection.Ascending);
    }

    public static ListSort Ascending(string column) => new(column, SortDirection.Ascending);

    public static ListSort Descending(string column) => new(column, SortDirection.Descending);

    /// <summary>The wire form, <c>name</c> or <c>-updated</c>: what <see cref="Parse"/> reads back.</summary>
    public override string ToString() => Direction == SortDirection.Descending ? $"-{Column}" : Column;
}

/// <summary>
/// Where the next page starts: the last row's value in the sort column (<c>null</c> when it had none) and its id,
/// which breaks ties. Typed by the column, so the keyset compares like with like.
/// </summary>
public sealed record ListPosition(object? Value, Guid Id);
