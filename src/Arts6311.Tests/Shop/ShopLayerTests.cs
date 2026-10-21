using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Shop.Business;
using Arts6311.Domain.Modules.Shop.Data;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.Validation;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;
using Arts6311.Tests.Infrastructure;
using Arts6311.Tests.Shop.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Shop;

/// <summary>
/// Each layer against fakes of the one below (backend.md "Testing"): the DataLayer's call order and failure mapping,
/// Business's rules and outcome mapping, and the facade's validation and its hand-offs to Media and Artists.
/// </summary>
public sealed class ShopLayerTests
{
    private const string AnyTag = "\"0123456789abcdef0123456789abcdef\"";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- DataLayer ----

    [Fact]
    public async Task TheDataLayerStagesTheSkuAndTheAuditRowBeforeItsOneSave()
    {
        var (dataLayer, repository, skus, _, log) = DataLayer();
        var item = new FakeShopDataLayer().Add();
        repository.Rows.Add(item);
        var before = item.RowVersion;

        var outcome = await dataLayer.UpdateAsync(
            item,
            new SkuChanges([new SkuReservation("ART-10", item.Id)], [new SkuReservation("ART-9", item.Id)]),
            "editor-2",
            "Updated: SKU.",
            pathChange: null,
            Ct);

        Assert.Equal(ShopWriteOutcome.Saved, outcome);

        // The release first, so a SKU this item gives up and takes again in one save is free when it is reserved.
        Assert.Equal(["sku-release:ART-9", "sku-reserve:ART-10", "audit:shop-item.updated", "save"], log.Calls);
        Assert.Equal(["ART-9"], skus.Released);
        Assert.Equal(("editor-2", Now), (item.UpdatedBy, item.UpdatedAt));
        Assert.NotEqual(before, item.RowVersion);
    }

    [Fact]
    public async Task APublishedSlugChangeAndEveryStatusChangeStageTheirMessageBeforeTheOneSave()
    {
        var (dataLayer, repository, _, _, log) = DataLayer();
        var item = new FakeShopDataLayer().Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));
        repository.Rows.Add(item);

        await dataLayer.UpdateAsync(
            item, SkuChanges.None, "editor-1", "Updated: slug.", new PublishedPathChange("/shop/a", "/shop/b"), Ct);
        log.Calls.Clear();
        await dataLayer.TransitionAsync(item, PublishCommand.Unpublish, PublishStatus.Published, "editor-1", Ct);

        Assert.Equal(["outbox:site.revalidate-requested", "audit:shop-item.unpublished", "save"], log.Calls);
    }

    [Fact]
    public async Task AFailedSaveIsMappedToTheClashItCanProveAndRethrownOtherwise()
    {
        var (dataLayer, repository, skus, _, _) = DataLayer();
        var item = new FakeShopDataLayer().Add();
        var reservation = new SkuChanges([new SkuReservation("ART-9", item.Id)], []);

        repository.SaveFailure = new DbUpdateConcurrencyException("moved");
        var conflict = await dataLayer.UpdateAsync(item, reservation, "editor-1", "Updated.", null, Ct);

        repository.SaveFailure = new DbUpdateException("unique");
        repository.SlugExists = true;
        var slugTaken = await dataLayer.UpdateAsync(item, reservation, "editor-1", "Updated.", null, Ct);

        repository.SaveFailure = new DbUpdateException("unique");
        repository.SlugExists = false;
        skus.Owners["ART-9"] = new SkuOwner(CatalogOwnerTypes.ClassRun, Guid.NewGuid());
        var skuTaken = await dataLayer.UpdateAsync(item, reservation, "editor-1", "Updated.", null, Ct);

        repository.SaveFailure = new DbUpdateException("something else");
        skus.Owners.Clear();

        Assert.Equal(ShopWriteOutcome.Conflict, conflict);
        Assert.Equal(ShopWriteOutcome.SlugTaken, slugTaken);
        Assert.Equal(ShopWriteOutcome.SkuTaken, skuTaken);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            dataLayer.UpdateAsync(item, reservation, "editor-1", "Updated.", null, Ct));
    }

    // ---- Business ----

    [Fact]
    public async Task BusinessSuggestsTheSkuFromTheItemNumberAndSkipsOneAlreadyTaken()
    {
        var (business, dataLayer) = Business();
        dataLayer.NextItemNumber = 17;
        dataLayer.SkuOwners["ART-17"] = new SkuOwner(CatalogOwnerTypes.ClassRun, Guid.NewGuid());

        var created = await business.CreateAsync(
            new ShopItemCreateViewModel("A Piece"), ShopItemMediaCheck.None, "editor-1", Ct);

        Assert.Equal("ART-18", created.Value!.Sku);
        Assert.Equal(["ART-18"], dataLayer.LastSkuChanges!.Reserve.Select(reservation => reservation.Sku));
        Assert.Empty(dataLayer.LastSkuChanges.Release);
    }

    [Fact]
    public async Task BusinessRefusesASkuAnotherRecordHolds()
    {
        var (business, dataLayer) = Business();
        dataLayer.SkuOwners["CLS-4-1"] = new SkuOwner(CatalogOwnerTypes.ClassRun, Guid.NewGuid());

        var taken = await business.CreateAsync(
            new ShopItemCreateViewModel("A Piece", Sku: "cls-4-1"), ShopItemMediaCheck.None, "editor-1", Ct);

        Assert.Equal(CatalogErrorCodes.SkuTaken, taken.Error!.Code);
        Assert.Contains("sku", taken.Error.FieldErrors.Keys);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessRefusesAnIllegalTransitionAndAnEditThatChangesNothingIsNotAWrite()
    {
        var (business, dataLayer) = Business();
        var item = dataLayer.Add(PublishStatus.Published, publishedAt: Now);
        var tag = Tag(item.RowVersion);

        var republished = await business.TransitionAsync(item.Id, PublishCommand.Publish, tag, null, "editor-1", Ct);
        var nothing = await business.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(Title: item.Title), tag, null, "editor-1", Ct);

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, republished.Error!.Code);
        Assert.True(nothing.Succeeded);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessTakesAScheduledPublishBackToDraftWhenPublishingIsNoLongerPossible()
    {
        var (business, dataLayer) = Business();
        var item = dataLayer.Add(PublishStatus.Scheduled, description: null, startingPriceCents: null);
        item.ScheduledFor = Now.AddMinutes(-1);

        var outcome = await business.PublishScheduledAsync(item.Id, Now, ShopItemMediaCheck.None, Ct);

        Assert.Equal(ScheduledPublishOutcome.Unscheduled, outcome);
        Assert.Equal((PublishStatus.Scheduled, PublishStatus.Draft), (dataLayer.LastAbandon!.Value.From, item.Status));
        Assert.Contains("ready to publish", dataLayer.LastAbandon.Value.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BusinessReportsLostWhenTheGalleryIsNoLongerTheOneMediaWasAskedAbout()
    {
        var (business, dataLayer) = Business();
        var photograph = Guid.NewGuid();
        var item = dataLayer.Add(PublishStatus.Scheduled, photograph: photograph);
        item.ScheduledFor = Now.AddMinutes(-1);

        // The answer is about an image this item no longer has: someone is mid-edit.
        var stale = new ShopItemMediaCheck(
            new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>(),
            new HashSet<Guid>(), new HashSet<Guid>());

        Assert.Equal(ScheduledPublishOutcome.Lost, await business.PublishScheduledAsync(item.Id, Now, stale, Ct));
    }

    [Fact]
    public async Task BusinessMapsTheDataLayersClashOutcomesToTheirCodes()
    {
        var (business, dataLayer) = Business();

        dataLayer.Outcome = ShopWriteOutcome.SlugTaken;
        var slug = await business.CreateAsync(
            new ShopItemCreateViewModel("A Piece"), ShopItemMediaCheck.None, "editor-1", Ct);

        dataLayer.Outcome = ShopWriteOutcome.SkuTaken;
        var sku = await business.CreateAsync(
            new ShopItemCreateViewModel("Another Piece"), ShopItemMediaCheck.None, "editor-1", Ct);

        Assert.Equal(ContentErrorCodes.SlugTaken, slug.Error!.Code);
        Assert.Equal(CatalogErrorCodes.SkuTaken, sku.Error!.Code);
    }

    // ---- Facade ----

    [Fact]
    public async Task TheFacadeRefusesAnInvalidListBeforeBusinessSeesIt()
    {
        var (facade, business, _, _) = Facade();

        var result = await facade.ListAsync(new ShopItemListViewModel(Category: "merch"), Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task TheFacadeAnswers428BeforeItValidatesTheBody()
    {
        var (facade, business, _, _) = Facade();

        var result = await facade.UpdateAsync(
            RecordingShopBusiness.Item.Id, new ShopItemUpdateViewModel(), ifMatch: null, "editor-1", Ct);

        Assert.Equal(ErrorCodes.PreconditionRequired, result.Error!.Code);
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task TheFacadeAsksMediaOnceAboutThePhotographsAndNotADescriptionPictureTheSanitizerDrops()
    {
        // ADR 0045: no pictures in a description at this time, so only the gallery is asked about.
        var (facade, business, media, _) = Facade();
        var photograph = Guid.NewGuid();
        var inDescription = Guid.NewGuid();
        media.MissingAltText.Add(photograph);

        var result = await facade.CreateAsync(
            new ShopItemCreateViewModel(
                "A Piece",
                Description: $"""<img src="/media/{inDescription}/800.webp" alt="A bench">""",
                ImageMediaIds: [photograph]),
            "editor-1",
            Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(1, media.Checks);
        Assert.Equal([photograph], business.CreateMedia!.AskedGallery);
        Assert.Equal([photograph], business.CreateMedia.GalleryWithoutAltText);
        Assert.Empty(business.CreateMedia.AskedDescription);
        Assert.Empty(business.CreateMedia.MissingGallery);
    }

    [Fact]
    public async Task TheFacadeRefusesAnArchivedArtistBeforeBusinessSeesIt()
    {
        var (facade, business, _, artists) = Facade();
        var retired = artists.Add("Retired Artist", PublishStatus.Archived);

        var refused = await facade.CreateAsync(new ShopItemCreateViewModel("A Piece", ArtistId: retired.Id), "editor-1", Ct);

        Assert.Contains("artistId", refused.Error!.FieldErrors.Keys);
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task ThePublishAndScheduleCommandsVouchForThePhotographsWithTwoBusinessCalls()
    {
        var (facade, business, _, _) = Facade();
        var when = Now.AddDays(1);

        await facade.PublishAsync(RecordingShopBusiness.Item.Id, AnyTag, "editor-1", Ct);
        await facade.ScheduleAsync(
            RecordingShopBusiness.Item.Id, new ContentScheduleViewModel(when), AnyTag, "editor-1", Ct);

        Assert.Equal(["get", "transition:Publish", "get", "schedule"], business.Calls);
        Assert.Equal(when, business.LastScheduledFor);
        Assert.NotNull(business.ScheduleMedia);
    }

    [Fact]
    public async Task TheListIsFinishedWithEachRowsArtistInOneQuestion()
    {
        var (facade, business, _, artists) = Facade();
        var carrie = artists.Add("Carrie Haase");
        business.Page.Add(Row("Copper Cuff", carrie.Id));
        business.Page.Add(Row("Iron Buffalo Ring", carrie.Id));
        business.Page.Add(Row("Chicago Collage Magazine Vol. 1", null));

        var page = await facade.ListAsync(new ShopItemListViewModel(), Ct);

        Assert.True(page.Succeeded, page.Error?.Message);
        Assert.Equal(
            ["Carrie Haase", "Carrie Haase", null],
            page.Value!.Items.Select(row => row.Artist?.Name));

        // The wire shape says which currency the row's amounts are in (api-contract.md).
        Assert.All(page.Value.Items, row => Assert.Equal("USD", row.Currency));

        // One question for the page, not one per row.
        Assert.Equal(1, artists.Lookups);
    }

    [Fact]
    public async Task AScheduledPublishOfARecordThatIsGoneIsSkipped()
    {
        var (facade, business, _, _) = Facade();

        var gone = await facade.PublishScheduledAsync(Guid.NewGuid(), Now, Ct);

        Assert.Equal(ScheduledPublishOutcome.Skipped, gone);
        Assert.Equal(["get"], business.Calls);
    }

    private static ShopItemListRow Row(string title, Guid? artistId) => new(
        Guid.NewGuid(),
        title,
        Slugs.FromName(title),
        PublishStatus.Draft,
        artistId,
        [],
        null,
        "ART-1",
        OneOfAKind: true,
        PriceCents: null,
        StockOnHand: null,
        SoldOut: false,
        DateTimeOffset.UnixEpoch);

    private static string Tag(Guid rowVersion) => $"\"{rowVersion:N}\"";

    private static (ShopDataLayer, FakeShopRepository, FakeSkuRegistry, RecordingAuditWriter, CallLog) DataLayer()
    {
        var log = new CallLog();
        var repository = new FakeShopRepository(log);
        var skus = new FakeSkuRegistry(log);
        var audit = new RecordingAuditWriter(log);
        return (
            new ShopDataLayer(repository, skus, audit, new RecordingOutboxWriter(log), new FixedCorrelation(), new FixedClock(Now)),
            repository,
            skus,
            audit,
            log);
    }

    private static (ShopBusiness, FakeShopDataLayer) Business()
    {
        var dataLayer = new FakeShopDataLayer();
        return (new ShopBusiness(dataLayer, new AllowListHtmlSanitizer(), new FixedClock(Now)), dataLayer);
    }

    private static (ShopFacade, RecordingShopBusiness, FakeMediaFacade, FakeArtistsFacade) Facade()
    {
        var business = new RecordingShopBusiness();
        var media = new FakeMediaFacade();
        var artists = new FakeArtistsFacade();
        var facade = new ShopFacade(
            business,
            media,
            artists,
            new AllowListHtmlSanitizer(),
            new ShopItemCreateValidator(),
            new ShopItemUpdateValidator(),
            new ShopItemListValidator(),
            new ContentScheduleValidator(),
            new SiteShopListValidator(),
            TestAppCache.Create());
        return (facade, business, media, artists);
    }
}
