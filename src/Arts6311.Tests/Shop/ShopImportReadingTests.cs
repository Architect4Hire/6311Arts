using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Shop.Managers;

namespace Arts6311.Tests.Shop;

/// <summary>
/// How a shop import reads a file before any rule is applied (ADR 0053): the header and the file-level limits
/// (<see cref="ShopImportSheet"/>), and what one cell means (<see cref="ShopImportCells"/>).
/// </summary>
public sealed class ShopImportReadingTests
{
    [Fact]
    public void RowsAreKeyedByAForgivingHeaderInAnyOrder()
    {
        var sheet = ShopImportSheet.Read("Starting Price, TITLE ,one-of-a-kind\r\n45, Bowl ,yes\r\n");

        Assert.True(sheet.Succeeded);
        var line = Assert.Single(sheet.Value!.Lines);
        Assert.Equal(2, line.Row);
        Assert.Equal(("Bowl", "45", "yes"), (line[ShopImportColumns.Title], line[ShopImportColumns.StartingPrice], line[ShopImportColumns.OneOfAKind]));

        // A column the file left out reads as blank.
        Assert.Equal(string.Empty, line[ShopImportColumns.Sku]);
    }

    [Fact]
    public void BlankRowsAndTrailingEmptyCellsAreIgnored()
    {
        var sheet = ShopImportSheet.Read("title,sku\n\nBowl,,,\n,\nCup,C-1\n");

        Assert.Equal([3, 5], sheet.Value!.Lines.Select(line => line.Row));
    }

    [Theory]
    [InlineData("title,price\nBowl,4", "price")]
    [InlineData("title,title\nBowl,Bowl", "more than once")]
    [InlineData("sku\nART-1", "no title column")]
    [InlineData("title\n", "no pieces")]
    [InlineData("   ", "empty")]
    [InlineData("title\nBowl,extra", "more cells than the header")]
    [InlineData("title\n\"Bowl", "never closed")]
    public void AFileTheImportCannotReadIsRefusedUnderCsv(string csv, string expected)
    {
        var sheet = ShopImportSheet.Read(csv);

        Assert.False(sheet.Succeeded);
        Assert.Equal(ErrorCodes.ValidationFailed, sheet.Error!.Code);
        var problem = Assert.Single(sheet.Error.FieldErrors[ShopImportSheet.Field]);
        Assert.Contains(expected, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreThanAThousandPiecesIsRefused()
    {
        var csv = "title\n" + string.Concat(Enumerable.Range(1, ShopPolicy.ImportMaxRows + 1).Select(n => $"Piece {n}\n"));

        var sheet = ShopImportSheet.Read(csv);

        Assert.Contains("more than 1,000 pieces", Assert.Single(sheet.Error!.FieldErrors[ShopImportSheet.Field]), StringComparison.Ordinal);
    }

    [Fact]
    public void ExactlyAThousandPiecesIsRead()
    {
        var csv = "title\n" + string.Concat(Enumerable.Range(1, ShopPolicy.ImportMaxRows).Select(n => $"Piece {n}\n"));

        Assert.Equal(ShopPolicy.ImportMaxRows, ShopImportSheet.Read(csv).Value!.Lines.Count);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("No", false)]
    [InlineData("0", false)]
    [InlineData("TRUE", true)]
    [InlineData("y", true)]
    [InlineData("1", true)]
    public void AFlagIsYesOrNo(string cell, bool expected)
    {
        Assert.Equal(expected, ShopImportCells.ReadFlag(cell));
    }

    [Fact]
    public void AFlagThatIsNeitherIsNull()
    {
        Assert.Null(ShopImportCells.ReadFlag("maybe"));
    }

    [Theory]
    [InlineData("45", 4500L)]
    [InlineData("45.5", 4550L)]
    [InlineData("$1,250.00", 125000L)]
    [InlineData("0", 0L)]
    [InlineData("50000", 5000000L)]
    public void APriceIsDollarsAsCents(string cell, long expected)
    {
        Assert.Equal((expected, (string?)null), ShopImportCells.ReadPrice(cell));
    }

    [Theory]
    [InlineData("4.999")]
    [InlineData("-5")]
    [InlineData("forty")]
    [InlineData("50000.01")]
    public void APriceThatIsNotOneIsRefused(string cell)
    {
        var (cents, error) = ShopImportCells.ReadPrice(cell);

        Assert.Null(cents);
        Assert.NotNull(error);
    }

    [Fact]
    public void ABlankPriceIsNoPriceYet()
    {
        Assert.Equal((null, null), ShopImportCells.ReadPrice(string.Empty));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("2.5")]
    [InlineData("ten")]
    public void ACountOutsideOneToAThousandIsRefused(string cell)
    {
        Assert.NotNull(ShopImportCells.ReadStock(cell).Error);
    }

    [Fact]
    public void CategoriesAreSlugsOrLabelsEachKeptOnce()
    {
        var slugs = ShopImportCells.ReadCategories("Prints | ceramics|PRINTS| Books and magazines |merch", out var unknown);

        Assert.Equal(["prints", "ceramics", "books-magazines"], slugs);
        Assert.Equal(["merch"], unknown);
    }

    [Fact]
    public void PlainTextBecomesParagraphsWithEverythingEncoded()
    {
        var html = ShopImportCells.ToHtml("Giclée <b>print</b>.\r\nSigned & numbered.\r\n\r\n  \r\nSecond paragraph.");

        Assert.Equal("<p>Giclée &lt;b&gt;print&lt;/b&gt;.<br>Signed &amp; numbered.</p><p>Second paragraph.</p>", html);
    }

    [Fact]
    public void AValueQuotedBackIsShortened()
    {
        var header = new string('x', 5_000);

        var problem = Assert.Single(ShopImportSheet.Read($"title,{header}\nBowl,1").Error!.FieldErrors[ShopImportSheet.Field]);
        var (_, priceError) = ShopImportCells.ReadPrice(new string('9', 5_000) + "x");

        Assert.True(problem.Length < 300, problem);
        Assert.True(priceError!.Length < 150, priceError);
    }
}
