using System.Globalization;
using Arts6311.Domain.Managers.Csv;
using Arts6311.Domain.Managers.Results;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// The columns a shop import reads (ADR 0053), as its header spells them. Only <see cref="Title"/> is required; the
/// rest may be left out of the file altogether, or left blank on a row. The admin's template uses these names.
/// </summary>
internal static class ShopImportColumns
{
    public const string Title = "title";
    public const string Sku = "sku";
    public const string Artist = "artist";
    public const string Categories = "categories";
    public const string OneOfAKind = "one_of_a_kind";
    public const string StartingPrice = "starting_price";
    public const string StartingStock = "starting_stock";
    public const string Slug = "slug";
    public const string Description = "description";

    /// <summary>Every column, in the template's order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Title, Sku, Artist, Categories, OneOfAKind, StartingPrice, StartingStock, Slug, Description];

    /// <summary>
    /// A header cell as a column name: trimmed, lower-case, with spaces and hyphens read as underscores, so
    /// "One of a kind" and "starting-price" are found too.
    /// </summary>
    public static string Normalize(string header) =>
        header.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
}

/// <summary>One row of an import file, its cells keyed by column. Nothing in it has been interpreted yet.</summary>
/// <param name="Row">Its row number in the spreadsheet: the header is row 1.</param>
internal sealed record ShopImportLine(int Row, IReadOnlyDictionary<string, string> Cells)
{
    /// <summary>The cell under <paramref name="column"/>, trimmed; empty when the file has no such column.</summary>
    public string this[string column] => Cells.TryGetValue(column, out var value) ? value.Trim() : string.Empty;
}

/// <summary>
/// An import file split into rows under its header (ADR 0053): the file-level checks — a header the import
/// understands, at least one row, no more than <see cref="ShopPolicy.ImportMaxRows"/> — and nothing about what any
/// row means, which is Business's to read.
/// </summary>
/// <remarks>
/// The facade reads the sheet before Business does, because it has to ask the Artists module about the artist column
/// first: cross-module questions are the facade's (backend.md). Both read the same sheet.
/// </remarks>
internal sealed record ShopImportSheet(IReadOnlyList<ShopImportLine> Lines)
{
    /// <summary>The field the file's own problems are reported under.</summary>
    public const string Field = "csv";

    public static OperationResult<ShopImportSheet> Read(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        // The header is a record too, so the limit is one more than the rows.
        var read = CsvReader.Read(csv, ShopPolicy.ImportMaxRows + 1);
        if (read.TooManyRecords)
        {
            return Invalid(string.Create(
                CultureInfo.InvariantCulture,
                $"The file has more than {ShopPolicy.ImportMaxRows:N0} pieces. Split it, and import each part in turn."));
        }

        if (read.Error is { } error)
        {
            return Invalid(error);
        }

        var header = read.Records.Count == 0 ? null : read.Records[0];
        if (header is null)
        {
            return Invalid("The file is empty.");
        }

        var columns = header.Fields.Select(ShopImportColumns.Normalize).ToList();
        var problems = new List<string>();

        var unknown = columns.Where(column => column.Length > 0 && !ShopImportColumns.All.Contains(column, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (unknown.Count > 0)
        {
            problems.Add($"The header has {(unknown.Count == 1 ? "a column" : "columns")} the import doesn't know: "
                + $"{Names(unknown)}. Use the template's: {string.Join(", ", ShopImportColumns.All)}.");
        }

        var repeated = columns.Where(column => column.Length > 0)
            .GroupBy(column => column, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (repeated.Count > 0)
        {
            problems.Add($"The header names {Names(repeated)} more than once.");
        }

        if (!columns.Contains(ShopImportColumns.Title, StringComparer.Ordinal))
        {
            problems.Add("The header has no title column. The first row must name the columns, as the template does.");
        }

        var lines = new List<ShopImportLine>();
        foreach (var record in read.Records.Skip(1))
        {
            // A spreadsheet may pad a row with empty cells past the last column; anything else there is data with
            // no column to go in.
            if (record.Fields.Skip(columns.Count).Any(field => !string.IsNullOrWhiteSpace(field)))
            {
                problems.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Row {record.Row} has more cells than the header has columns. Check for a comma that needs quotes around its cell."));
                continue;
            }

            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < columns.Count && index < record.Fields.Count; index++)
            {
                if (columns[index].Length > 0)
                {
                    cells[columns[index]] = record.Fields[index];
                }
            }

            lines.Add(new ShopImportLine(record.Row, cells));
        }

        if (problems.Count == 0 && lines.Count == 0)
        {
            problems.Add("The file has a header but no pieces under it.");
        }

        return problems.Count == 0
            ? OperationResult.Success(new ShopImportSheet(lines))
            : OperationResult.Failure<ShopImportSheet>(OperationError.Validation(
                "The file can't be imported.", problems.Select(problem => (Field, problem))));
    }

    /// <summary>
    /// Header cells to quote back, each shortened and only the first few: a header cell is whatever the file says,
    /// and the answer should not be as long as the file.
    /// </summary>
    private static string Names(List<string> names)
    {
        const int Shown = 10;
        var listed = string.Join(", ", names.Take(Shown).Select(ShopImportCells.Shorten));
        return names.Count <= Shown
            ? listed
            : string.Create(CultureInfo.InvariantCulture, $"{listed} and {names.Count - Shown} more");
    }

    private static OperationResult<ShopImportSheet> Invalid(string problem) =>
        OperationResult.Failure<ShopImportSheet>(
            OperationError.Validation("The file can't be imported.", [(Field, problem)]));
}
