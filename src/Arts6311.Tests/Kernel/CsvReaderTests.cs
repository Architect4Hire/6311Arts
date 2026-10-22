using Arts6311.Domain.Managers.Csv;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// The kernel's CSV reader: RFC 4180 as spreadsheets write it, and row numbers a person can find in the spreadsheet
/// they saved (ADR 0053).
/// </summary>
public sealed class CsvReaderTests
{
    [Fact]
    public void FieldsAreSplitOnCommasAndRowsOnAnyLineEnding()
    {
        var read = CsvReader.Read("a,b\r\nc,d\ne,f\rg,h", 10);

        Assert.Null(read.Error);
        Assert.Equal([["a", "b"], ["c", "d"], ["e", "f"], ["g", "h"]], read.Records.Select(record => record.Fields));
        Assert.Equal([1, 2, 3, 4], read.Records.Select(record => record.Row));
    }

    [Fact]
    public void AQuotedFieldKeepsItsCommasLineBreaksAndDoubledQuotes()
    {
        var read = CsvReader.Read("title,description\r\n\"Bowl, raku\",\"Line one\r\n\r\nSaid \"\"hi\"\"\"\r\nNext,row\r\n", 10);

        Assert.Null(read.Error);
        Assert.Equal(["Bowl, raku", "Line one\r\n\r\nSaid \"hi\""], read.Records[1].Fields);

        // Counted as records, not text lines: the description's line breaks don't move the next row's number.
        Assert.Equal(3, read.Records[2].Row);
    }

    [Fact]
    public void AByteOrderMarkIsDropped()
    {
        var read = CsvReader.Read("﻿title\nBowl", 10);

        Assert.Equal("title", read.Records[0].Fields[0]);
    }

    [Fact]
    public void EmptyFieldsAndAFinalLineWithoutABreakAreRead()
    {
        var read = CsvReader.Read("a,,c\nd,,", 10);

        Assert.Equal([["a", "", "c"], ["d", "", ""]], read.Records.Select(record => record.Fields));
    }

    [Fact]
    public void AQuoteThatNeverClosesIsAnErrorNamingTheRow()
    {
        var read = CsvReader.Read("a\n\"open", 10);

        Assert.Empty(read.Records);
        Assert.StartsWith("Row 2 ", read.Error);
    }

    [Fact]
    public void TextAfterAClosingQuoteIsAnError()
    {
        var read = CsvReader.Read("\"a\"b,c", 10);

        Assert.StartsWith("Row 1 ", read.Error);
    }

    [Fact]
    public void TooManyRecordsStopsTheRead()
    {
        var read = CsvReader.Read("a\nb\nc\nd", 3);

        Assert.True(read.TooManyRecords);
        Assert.Empty(read.Records);
    }

    [Fact]
    public void BlankLinesAreSkippedButKeepTheRowNumbers()
    {
        var read = CsvReader.Read("a\n\n\nb\n , \nc\n", 3);

        Assert.False(read.TooManyRecords);
        Assert.Equal([1, 4, 6], read.Records.Select(record => record.Row));
    }

    [Fact]
    public void AFileOfNothingButLineBreaksKeepsNoRecords()
    {
        // A blank record costs a counter, not an object, so two million line breaks hold nothing in memory.
        var read = CsvReader.Read(new string('\n', 2_000_000), 3);

        Assert.Null(read.Error);
        Assert.Empty(read.Records);
    }

    [Fact]
    public void WhatTheWriterWritesTheReaderReadsBack()
    {
        string[] header = ["name", "note"];
        string[][] rows = [["Jane \"JD\" Doe", "one, two\nthree"]];

        var read = CsvReader.Read(CsvFormat.Write(header, rows), 10);

        Assert.Equal(header, read.Records[0].Fields);
        Assert.Equal(rows[0], read.Records[1].Fields);
    }
}
