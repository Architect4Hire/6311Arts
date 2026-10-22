using System.Globalization;
using System.Text;

namespace Arts6311.Domain.Managers.Csv;

/// <summary>One record of a CSV file, with the row number a spreadsheet shows it on (the header is row 1).</summary>
internal sealed record CsvRecord(int Row, IReadOnlyList<string> Fields);

/// <summary>What <see cref="CsvReader.Read"/> made of a file: its records, or why it couldn't be read.</summary>
/// <param name="TooManyRecords">The file went past the limit, so <paramref name="Records"/> is empty; the caller says so in its own words.</param>
internal sealed record CsvReadResult(IReadOnlyList<CsvRecord> Records, string? Error, bool TooManyRecords = false);

/// <summary>
/// RFC 4180 CSV as spreadsheets write it, the reading half of <see cref="CsvFormat"/>: fields separated by commas,
/// quoted when they hold a comma, a quote or a line break, with a quote inside doubled. Lines may end CRLF, LF or CR;
/// a leading byte-order mark is dropped (Excel's "CSV UTF-8" writes one).
/// </summary>
/// <remarks>
/// Each record's <see cref="CsvRecord.Row"/> counts records, not text lines, so a description with line breaks in it
/// is still one row — the number a person sees down the side of the spreadsheet they saved it from. A blank record
/// (every field empty or whitespace) keeps its row number but is never returned, or kept: a file of nothing but line
/// breaks costs a counter, not a record each.
/// </remarks>
internal static class CsvReader
{
    private const char ByteOrderMark = '﻿';

    /// <param name="text">The whole file, already decoded.</param>
    /// <param name="maxRecords">
    /// Reading stops with an error past this many records that are not blank, so a huge file costs no more than this.
    /// </param>
    public static CsvReadResult Read(string text, int maxRecords)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecords);

        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var position = text.Length > 0 && text[0] == ByteOrderMark ? 1 : 0;
        var quoted = false;
        var afterQuote = false;
        var startedRecord = false;
        var row = 0;

        while (position < text.Length)
        {
            var current = text[position++];

            if (quoted)
            {
                if (current != '"')
                {
                    field.Append(current);
                }
                else if (position < text.Length && text[position] == '"')
                {
                    field.Append('"');
                    position++;
                }
                else
                {
                    quoted = false;
                    afterQuote = true;
                }

                continue;
            }

            switch (current)
            {
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    afterQuote = false;
                    startedRecord = true;
                    break;

                case '\r' or '\n':
                    if (current == '\r' && position < text.Length && text[position] == '\n')
                    {
                        position++;
                    }

                    fields.Add(field.ToString());
                    field.Clear();
                    afterQuote = false;
                    startedRecord = false;
                    if (Add(records, fields, ref row) > maxRecords)
                    {
                        return TooMany(maxRecords);
                    }

                    break;

                case '"' when field.Length == 0 && !afterQuote:
                    quoted = true;
                    startedRecord = true;
                    break;

                default:
                    if (afterQuote)
                    {
                        return Failure(row + 1, "has text straight after a closing quote. Put the whole field in quotes, and double any quote inside it (\"\").");
                    }

                    field.Append(current);
                    startedRecord = true;
                    break;
            }
        }

        if (quoted)
        {
            return Failure(row + 1, "opens a quote that is never closed.");
        }

        // The last line, when the file does not end with a line break.
        if (startedRecord || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            if (Add(records, fields, ref row) > maxRecords)
            {
                return TooMany(maxRecords);
            }
        }

        return new CsvReadResult(records, null);
    }

    /// <summary>Ends a record — kept unless it is blank — and answers how many have been kept so far.</summary>
    private static int Add(List<CsvRecord> records, List<string> fields, ref int row)
    {
        row++;
        if (!fields.All(string.IsNullOrWhiteSpace))
        {
            records.Add(new CsvRecord(row, [.. fields]));
        }

        fields.Clear();
        return records.Count;
    }

    private static CsvReadResult Failure(int row, string problem) =>
        new([], string.Create(CultureInfo.InvariantCulture, $"Row {row} {problem}"));

    private static CsvReadResult TooMany(int maxRecords) =>
        new([], string.Create(CultureInfo.InvariantCulture, $"The file has more than {maxRecords:N0} records."), TooManyRecords: true);
}
