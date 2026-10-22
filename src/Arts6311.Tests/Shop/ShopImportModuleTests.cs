using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Arts6311.Tests.Shop.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Shop;

/// <summary>
/// The CSV import end to end through its facade, on a real (SQLite) database (ADR 0053): the preview, the rows read
/// against each other and against what is stored, all-or-nothing, the idempotent replay, and what Square owns.
/// </summary>
public sealed class ShopImportModuleTests
{
    private const string Editor = ShopTestHost.EditorId;

    private const string Header = "title,sku,artist,categories,one_of_a_kind,starting_price,starting_stock,slug,description";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryRowBecomesADraftWithItsSkuReservedAndAnAuditRow()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var artist = await host.AddArtistAsync("Jane Doe");

        var result = await ImportAsync(host, $"""
            {Header}
            Blue Heron,PRT-42,jane doe,Prints,false,$45.00,10,,"Giclée print.

            Signed & numbered."
            Raku Bowl,,,ceramics,yes,180,,,
            """);

        Assert.Equal(2, result.CreatedCount);
        Assert.Equal([(2, "Blue Heron", "blue-heron", "PRT-42"), (3, "Raku Bowl", "raku-bowl", "ART-2")],
            result.Items.Select(item => (item.Row, item.Title, item.Slug, item.Sku)));

        var heron = await host.StoredAsync(result.Items[0].Id);
        Assert.Equal(PublishStatus.Draft, heron!.Status);
        Assert.Equal(artist, heron.ArtistId);
        Assert.Equal(["prints"], heron.Categories.Select(row => row.Category));
        Assert.Equal((4500L, 10, false), (heron.StartingPriceCents, heron.StartingStockQuantity, heron.OneOfAKind));
        Assert.Equal("<p>Giclée print.</p><p>Signed &amp; numbered.</p>", heron.Description);

        // One of a kind with no count given is a count of one, as a single create makes it.
        var bowl = await host.StoredAsync(result.Items[1].Id);
        Assert.Equal((18000L, 1, true), (bowl!.StartingPriceCents, bowl.StartingStockQuantity, bowl.OneOfAKind));

        Assert.Equal(["ART-2", "PRT-42"], (await host.SkusAsync()).Select(row => row.Sku));
        Assert.All(await host.SkusAsync(), row => Assert.Equal(CatalogOwnerTypes.ShopItem, row.OwnerType));
        Assert.Equal(["shop-item.imported", "shop-item.imported"], await host.AuditActionsAsync());
        var summaries = await host.QueryAsync(context => context.AuditLogs.AsNoTracking().Select(entry => entry.Summary).ToListAsync(Ct));
        Assert.Contains("Imported as Draft from artwork.csv, row 3.", summaries);
    }

    [Fact]
    public async Task NothingGoesToTheOutboxOrIntoWhatSquareOwns()
    {
        // A Draft is on no page and in no Square catalog until someone publishes it (ADR 0032, 0053).
        await using var host = await ShopTestHost.CreateAsync(Ct);

        var result = await ImportAsync(host, $"{Header}\nCopper Cuff,,,jewelry,true,110,1,,A cuff.");

        var item = await host.StoredAsync(Assert.Single(result.Items).Id);
        Assert.Null(item!.PriceCents);
        Assert.Null(item.StockOnHand);
        Assert.False(item.SoldOut);
        Assert.Empty(await host.QueryAsync(context => context.Set<OutboxMessage>().AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task APreviewShowsWhatWouldBeCreatedAndWritesNothing()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await host.AddItemAsync("Existing", itemNumber: 1, sku: "ART-2");

        var preview = await PreviewAsync(host, $"{Header}\nOne,,,,,,,,\nTwo,,,,,,,,");

        Assert.Equal((2, 0), (preview.RowCount, preview.ErrorCount));

        // Item numbers 2 and 3; ART-2 is taken, so the first suggestion moves on and the second follows it.
        Assert.Equal([("ART-3", true), ("ART-4", true)], preview.Rows.Select(row => (row.Sku, row.SkuSuggested)));
        Assert.Single(await host.QueryAsync(context => context.Set<ShopItem>().AsNoTracking().ToListAsync(Ct)));
        Assert.Single(await host.SkusAsync());
    }

    [Fact]
    public async Task EveryProblemInEveryRowIsReportedAtOnce()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await host.AddArtistAsync("Sam Lee");
        await AddNamesakeAsync(host, "Sam Lee", "sam-lee-2");
        await host.AddArtistAsync("Old Hand", PublishStatus.Archived);
        await host.AddItemAsync("Taken", sku: "TAKEN-1");

        var preview = await PreviewAsync(host, $"""
            {Header}
            ,bad sku!,Nobody,merch,maybe,forty,0,Bad Slug,
            Cup,TAKEN-1,Sam Lee,,true,10,3,,
            Plate,DUP-1,Old Hand,,,,,,
            Saucer,dup-1,,,,,,,
            """);

        Assert.Equal((4, 4), (preview.RowCount, preview.ErrorCount));
        Assert.Equal(
            ["title", "slug", "sku", "artist", "categories", "one_of_a_kind", "starting_price", "starting_stock"],
            preview.Rows[0].Errors.Select(error => error.Field));
        // What a row says on its own is read first; what the registry says about its SKU follows.
        Assert.Equal(["artist", "starting_stock", "sku"], preview.Rows[1].Errors.Select(error => error.Field));
        Assert.Contains("2 artists are called", preview.Rows[1].Errors[0].Message, StringComparison.Ordinal);
        Assert.Contains("archived", Assert.Single(preview.Rows[2].Errors).Message, StringComparison.Ordinal);

        // SKUs are compared as the registry stores them: "dup-1" is row 4's DUP-1 again.
        Assert.Contains("Row 4 already uses", Assert.Single(preview.Rows[3].Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneBadRowStopsTheWholeFileAndSaysWhereByRowAndColumn()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);

        var outcome = await host.WithImportFacadeAsync(facade => facade.ImportAsync(
            new ShopImportViewModel($"{Header}\nGood,,,,,,,,\nBad,,,,,12.345,,,"), "key-1", Editor, Ct));

        Assert.False(outcome.Result.Succeeded);
        Assert.Equal(ErrorCodes.ValidationFailed, outcome.Result.Error!.Code);
        Assert.Equal(["rows[3].starting_price"], outcome.Result.Error.FieldErrors.Keys);
        Assert.Empty(await host.QueryAsync(context => context.Set<ShopItem>().AsNoTracking().ToListAsync(Ct)));
        Assert.Empty(await host.SkusAsync());
    }

    [Fact]
    public async Task AGivenSlugIsKeptOrRefusedButAMadeOneTakesTheNextNumber()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await host.ExecuteAsync(async context =>
        {
            context.Add(new ShopItem
            {
                Id = Guid.NewGuid(),
                Title = "Untitled",
                Slug = "untitled",
                Sku = "OLD-1",
                ItemNumber = 1,
                UpdatedBy = Editor,
                RowVersion = Guid.NewGuid(),
            });
            await context.SaveChangesAsync(Ct);
        });

        var preview = await PreviewAsync(host, $"""
            {Header}
            Untitled,,,,,,,,
            Untitled,,,,,,,untitled-2,
            Untitled,,,,,,,,
            Other,,,,,,,untitled,
            """);

        // The given untitled-2 is claimed first, so the made ones step round it.
        Assert.Equal(["untitled-3", "untitled-2", "untitled-4", "untitled"], preview.Rows.Select(row => row.Slug));
        Assert.Empty(preview.Rows[1].Errors);
        Assert.Equal("slug", Assert.Single(preview.Rows[3].Errors).Field);
    }

    [Fact]
    public async Task AnArtistIsFoundBySlugBeforeName()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var jane = await host.AddArtistAsync("Jane Doe");

        var preview = await PreviewAsync(host, $"{Header}\nA,,jane-doe,,,,,,\nB,,JANE DOE,,,,,,\nC,,  Jane Doe ,,,,,,");

        Assert.All(preview.Rows, row => Assert.Equal(jane, row.Artist!.Id));
    }

    [Fact]
    public async Task ADeletedPiecesAddressAndSkuAreFreeForAnImport()
    {
        // ADR 0052: deleting releases both, so an import can reuse them as a single create can.
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var created = await ImportAsync(host, $"{Header}\nCopper Cuff,CUFF-1,,,,,,,");
        var stored = await host.StoredAsync(created.Items[0].Id);
        Assert.True((await host.WithFacadeAsync(facade => facade.DeleteAsync(
            stored!.Id, EntityTag.Format(stored.RowVersion), ShopTestHost.AdminId, Ct))).Succeeded);

        var again = await ImportAsync(host, $"{Header}\nCopper Cuff,CUFF-1,,,,,,,", key: "key-2");

        Assert.Equal(("copper-cuff", "CUFF-1"), (again.Items[0].Slug, again.Items[0].Sku));
    }

    [Fact]
    public async Task ARetryWithTheSameKeyReplaysInsteadOfImportingTwice()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var csv = $"{Header}\nOne,,,,,,,,\nTwo,,,,,,,,";

        var first = await host.WithImportFacadeAsync(facade => facade.ImportAsync(new ShopImportViewModel(csv), "key-1", Editor, Ct));
        var second = await host.WithImportFacadeAsync(facade => facade.ImportAsync(new ShopImportViewModel(csv, "renamed.csv"), "key-1", Editor, Ct));

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Result.Value!.Items.Select(item => item.Id), second.Result.Value!.Items.Select(item => item.Id));
        Assert.Equal(2, (await host.QueryAsync(context => context.Set<ShopItem>().AsNoTracking().ToListAsync(Ct))).Count);
    }

    [Fact]
    public async Task TheSameKeyWithAnotherFileIsRefused()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await ImportAsync(host, $"{Header}\nOne,,,,,,,,");

        var outcome = await host.WithImportFacadeAsync(facade => facade.ImportAsync(
            new ShopImportViewModel($"{Header}\nTwo,,,,,,,,"), "key-1", Editor, Ct));

        Assert.Equal(ErrorCodes.IdempotencyKeyReused, outcome.Result.Error!.Code);
    }

    [Fact]
    public async Task AnImportWithoutAKeyIsRefusedBeforeAnythingIsWritten()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);

        var outcome = await host.WithImportFacadeAsync(facade => facade.ImportAsync(
            new ShopImportViewModel($"{Header}\nOne,,,,,,,,"), null, Editor, Ct));

        Assert.Equal(ErrorCodes.IdempotencyKeyRequired, outcome.Result.Error!.Code);
        Assert.Empty(await host.QueryAsync(context => context.Set<ShopItem>().AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task AFailedSaveLeavesNoItemSkuOrIdempotencyRecord()
    {
        var failAudit = new FailInsertInto("AuditLogs");
        await using var host = await ShopTestHost.CreateAsync(Ct, failAudit);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithImportFacadeAsync(facade => facade.ImportAsync(
            new ShopImportViewModel($"{Header}\nOne,,,,,,,,\nTwo,,,,,,,,"), "key-1", Editor, Ct)));

        Assert.Empty(await host.QueryAsync(context => context.Set<ShopItem>().AsNoTracking().ToListAsync(Ct)));
        Assert.Empty(await host.SkusAsync());
        Assert.Empty(await host.QueryAsync(context =>
            context.Set<IdempotencyRecord>().AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task AFileThatIsNotUtf8IsRefusedUnderCsv()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);

        var preview = await host.WithImportFacadeAsync(facade => facade.PreviewAsync(
            new ShopImportViewModel($"{Header}\nGicl\uFFFDe,,,,,,,,"), Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, preview.Error!.Code);
        Assert.Contains("CSV UTF-8", Assert.Single(preview.Error.FieldErrors["csv"]), StringComparison.Ordinal);
    }

    /// <summary>A second artist with a name already on the roster: names are not unique, slugs are.</summary>
    private static Task AddNamesakeAsync(ShopTestHost host, string name, string slug) =>
        host.ExecuteAsync(async context =>
        {
            context.Add(new Artist
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = slug,
                Active = true,
                Status = PublishStatus.Published,
                CreatedAt = host.Time.GetUtcNow(),
                UpdatedAt = host.Time.GetUtcNow(),
                UpdatedBy = Editor,
                RowVersion = Guid.NewGuid(),
            });
            await context.SaveChangesAsync(Ct);
        });

    private static async Task<ShopImportPreviewServiceModel> PreviewAsync(ShopTestHost host, string csv)
    {
        var result = await host.WithImportFacadeAsync(facade => facade.PreviewAsync(new ShopImportViewModel(csv), Ct));
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value!;
    }

    private static async Task<ShopImportResultServiceModel> ImportAsync(ShopTestHost host, string csv, string key = "key-1")
    {
        var outcome = await host.WithImportFacadeAsync(facade => facade.ImportAsync(
            new ShopImportViewModel(csv, "artwork.csv"), key, Editor, Ct));
        Assert.True(outcome.Result.Succeeded, outcome.Result.Error is { } error
            ? $"{error.Message} {string.Join("; ", error.FieldErrors.Select(pair => $"{pair.Key}: {string.Join(" ", pair.Value)}"))}"
            : null);
        return outcome.Result.Value!;
    }
}
