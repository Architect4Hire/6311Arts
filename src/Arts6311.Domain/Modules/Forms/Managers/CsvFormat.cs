using System.Text;

namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>
/// RFC 4180 CSV, safe to open in a spreadsheet: every field is quoted, and one a spreadsheet would read as a formula
/// (it starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return) is prefixed with an apostrophe
/// (OWASP "CSV injection"). The values come from anonymous visitors, and staff open the file in Excel.
/// </summary>
internal static class CsvFormat
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        AppendRow(text, header);
        foreach (var row in rows)
        {
            AppendRow(text, row);
        }

        return text.ToString();
    }

    public static string Field(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var safe = value.Length > 0 && FormulaStarts.Contains(value[0]) ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void AppendRow(StringBuilder text, IReadOnlyList<string> row)
    {
        text.AppendJoin(',', row.Select(Field));
        text.Append("\r\n");
    }
}
