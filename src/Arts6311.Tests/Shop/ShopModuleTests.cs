using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Arts6311.Tests.Shop.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Arts6311.Domain.Managers.Concurrency;

namespace Arts6311.Tests.Shop;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: the rules, the credit across the Artists
/// boundary, the SKU it claims in the shared registry — including against a class session — the audit trail, the
/// atomic save, and what Square owns and the CMS may not write.
/// </summary>
public sealed class ShopModuleTests
{
    private const string Editor = ShopTestHost.EditorId;

    private const string Admin = ShopTestHost.AdminId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreatedItemIsADraftWithASuggestedSkuReservedAndAnAuditRow()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var artist = await host.AddArtistAsync("Carrie Haase");
        var front = await host.AddImageAsync("The cuff from the front.");
        var side = await host.AddImageAsync("The cuff from the side.");

        var item = await CreateAsync(host, new ShopItemCreateViewModel(
            "  Copper Cuff ",
            Description: "<p>A forged copper cuff.</p>",
            ArtistId: artist,
            Categories: ["jewelry"],
            ImageMediaIds: [side, front],
            OneOfAKind: true,
            StartingPriceCents: 11000));

        Assert.Equal(("Copper Cuff", "copper-cuff", PublishStatus.Draft), (item.Title, item.Slug, item.Status));
        Assert.Equal("ART-1", item.Sku);
        Assert.Equal(1, item.ItemNumber);
        Assert.Equal("Carrie Haase", item.Artist!.Name);
        Assert.Equal(["jewelry"], item.Categories);

        // The gallery keeps the order it was sent in: the first is the lead (ADR 0038).
        Assert.Equal([side, front], item.ImageMediaIds);
        Assert.Equal((11000L, 1, "USD"), (item.StartingPriceCents, item.StartingStockQuantity, item.Currency));
        Assert.True(item.StartingPriceEditable && item.StartingStockEditable);
        Assert.Null(item.PriceCents);
        Assert.Null(item.StockOnHand);
        Assert.False(item.SquareHasRecord);

        var row = Assert.Single(await host.SkusAsync());
        Assert.Equal(("ART-1", CatalogOwnerTypes.ShopItem, item.Id), (row.Sku, row.OwnerType, row.OwnerId));
        Assert.Equal(["shop-item.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AMadeSlugTakesTheNextFreeNumberButAGivenTakenOneIsRefusedOnTheSlugField()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ShopItemCreateViewModel("Copper Cuff"));

        // The CMS never sends a slug, so one made from the name can't be refused: there is nothing staff could change.
        var second = await CreateAsync(host, new ShopItemCreateViewModel("COPPER CUFF"));
        var third = await CreateAsync(host, new ShopItemCreateViewModel("Copper cuff"));
        var given = await host.WithFacadeAsync(facade => facade.CreateAsync(new ShopItemCreateViewModel("Silver Ring", Slug: "copper-cuff"), Editor, Ct));

        Assert.Equal(("copper-cuff-2", "copper-cuff-3"), (second.Slug, third.Slug));
        Assert.Equal(ContentErrorCodes.SlugTaken, given.Error!.Code);
        Assert.Contains("slug", given.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task AShopItemCannotTakeTheSkuAClassRunHolds()
    {
        // ADR 0037's cross-type claim, with two real owners rather than two rows written by hand.
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var run = new ClassRunViewModel(10);
        var offering = await host.WithClassesFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Intro to Screen Printing", Runs: [run]), Editor, Ct));
        Assert.True(offering.Succeeded, offering.Error?.Message);

        var refused = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ShopItemCreateViewModel("Copper Cuff", Sku: "cls-1-1"), Editor, Ct));

        Assert.Equal(CatalogErrorCodes.SkuTaken, refused.Error!.Code);
        Assert.Contains("sku", refused.Error.FieldErrors.Keys);
        Assert.Contains("CLS-1-1", refused.Error.Message);
        Assert.Single(await host.SkusAsync());
    }

    [Fact]
    public async Task AClassRunCannotTakeTheSkuAShopItemHolds()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ShopItemCreateViewModel("Copper Cuff"));

        var refused = await host.WithClassesFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel(
                "Intro to Screen Printing",
                Runs: [new ClassRunViewModel(10, Sku: "ART-1")]),
            Editor,
            Ct));

        Assert.Equal(CatalogErrorCodes.SkuTaken, refused.Error!.Code);
        Assert.Single(await host.SkusAsync());
    }

    [Fact]
    public async Task OneOfAKindIsAlwaysACountOfOne()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);

        // The count follows the flag when none is sent, and a count other than one is refused rather than quietly
        // replaced: nobody types 9 and finds a 1.
        var unique = await CreateAsync(host, new ShopItemCreateViewModel("Ammonite Necklace", OneOfAKind: true));
        var refused = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ShopItemCreateViewModel("Second Necklace", OneOfAKind: true, StartingStockQuantity: 12), Editor, Ct));
        var edition = await CreateAsync(host, new ShopItemCreateViewModel(
            "Mushrooms in Fall", OneOfAKind: false, StartingStockQuantity: 25));

        Assert.Equal(1, unique.StartingStockQuantity);
        Assert.Contains("startingStockQuantity", refused.Error!.FieldErrors.Keys);
        Assert.Equal(25, edition.StartingStockQuantity);

        // Flipping the flag on an edition takes its count with it.
        var flipped = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            edition.Id, new ShopItemUpdateViewModel(OneOfAKind: true), edition.ETag, Editor, Ct)));

        Assert.True(flipped.OneOfAKind);
        Assert.Equal(1, flipped.StartingStockQuantity);
    }

    [Fact]
    public async Task PublishingNeedsADescriptionAPriceACountAndADescribedPhotograph()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var bare = await CreateAsync(host, new ShopItemCreateViewModel("Bare Piece", OneOfAKind: false));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(bare.Id, bare.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Equal(
            ["description", "imageMediaIds", "startingPriceCents", "startingStockQuantity"],
            refused.Error.FieldErrors.Keys.Order(StringComparer.Ordinal));

        // A photograph nobody has described is refused too, and named as such.
        var undescribed = await host.AddImageAsync(altText: null);
        var withImage = await CreateAsync(host, new ShopItemCreateViewModel(
            "Described Later",
            Description: "<p>A piece.</p>",
            ImageMediaIds: [undescribed],
            OneOfAKind: true,
            StartingPriceCents: 1000));
        var noAltText = await host.WithFacadeAsync(facade => facade.PublishAsync(withImage.Id, withImage.ETag, Editor, Ct));

        Assert.Contains("imageMediaIds", noAltText.Error!.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(withImage.Id))!.Status);
    }

    [Fact]
    public async Task PublishingStagesOneRevalidationForItsOwnPage()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var item = await Ready(host);

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(item.Id, item.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.True(published.SlugLocked);

        var message = Assert.Single(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync(Ct)));
        Assert.Equal(SiteRevalidateRequested.MessageType, message.Type);
        var staged = SiteRevalidateRequested.TryParse(message.PayloadJson)!;
        Assert.Equal(("shop-item", item.Id), (staged.EntityType, staged.EntityId));
        Assert.Equal([$"/shop/{published.Slug}"], staged.Paths);
        Assert.Equal(["shop-item.created", "shop-item.published"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ARenameAfterPublishStagesItsRedirect()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var item = await Ready(host);
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(item.Id, item.ETag, Editor, Ct)));

        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(Slug: "the-copper-cuff"), published.ETag, Editor, Ct)));

        Assert.Equal("the-copper-cuff", renamed.Slug);
        var payload = Assert.Single(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == RedirectSlugChanged.MessageType)
            .Select(message => message.PayloadJson).ToListAsync(Ct)));
        var change = RedirectSlugChanged.TryParse(payload)!;
        Assert.Equal(
            ("shop-item", $"/shop/{published.Slug}", "/shop/the-copper-cuff"),
            (change.ContentType, change.FromPath, change.ToPath));
    }

    [Fact]
    public async Task AStaleOrMissingIfMatchIsRefusedBeforeAnythingIsWritten()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var item = await Ready(host);

        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(Title: "Renamed"), "\"0123456789abcdef0123456789abcdef\"", Editor, Ct));
        var missing = await host.WithFacadeAsync(facade => facade.PublishAsync(item.Id, null, Editor, Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        Assert.Equal("Copper Cuff", (await host.StoredAsync(item.Id))!.Title);
    }

    [Fact]
    public async Task WhatSquareOwnsIsNeverWrittenFromTheCms()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var item = await Ready(host);

        // As the 5.x inbound pull would leave it: Square has the price and three in stock.
        await SquareReports(host, item.Id, priceCents: 12000, stockOnHand: 3);
        var current = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(item.Id, Ct)));

        var priced = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(StartingPriceCents: 20000), current.ETag, Editor, Ct));
        var counted = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(StartingStockQuantity: 9), current.ETag, Editor, Ct));
        var flipped = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(OneOfAKind: false), current.ETag, Editor, Ct));

        Assert.False(current.StartingPriceEditable);
        Assert.False(current.StartingStockEditable);
        Assert.Equal((12000L, 3, true), (current.PriceCents, current.StockOnHand, current.SquareHasRecord));
        Assert.Contains("startingPriceCents", priced.Error!.FieldErrors.Keys);
        Assert.Contains("startingStockQuantity", counted.Error!.FieldErrors.Keys);
        Assert.Contains("oneOfAKind", flipped.Error!.FieldErrors.Keys);

        var stored = (await host.StoredAsync(item.Id))!;
        Assert.Equal((11000L, 1, true), (stored.StartingPriceCents, stored.StartingStockQuantity, stored.OneOfAKind));
        Assert.Equal((12000L, 3), (stored.PriceCents, stored.StockOnHand));
    }

    [Fact]
    public async Task TheGalleryIsReorderedAndACategorySetIsReplaced()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var front = await host.AddImageAsync("Front.");
        var side = await host.AddImageAsync("Side.");
        var item = await CreateAsync(host, new ShopItemCreateViewModel(
            "Copper Cuff", ImageMediaIds: [front, side], Categories: ["jewelry"]));

        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id,
            new ShopItemUpdateViewModel(ImageMediaIds: [side, front], Categories: ["jewelry", "sculpture"]),
            item.ETag,
            Editor,
            Ct)));

        Assert.Equal([side, front], edited.ImageMediaIds);
        Assert.Equal(["jewelry", "sculpture"], edited.Categories);

        var stored = (await host.StoredAsync(item.Id))!;
        Assert.Equal([side, front], stored.Images.OrderBy(image => image.Position).Select(image => image.MediaAssetId));
        Assert.Equal([0, 1], stored.Images.OrderBy(image => image.Position).Select(image => image.Position));

        var cleared = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(Categories: []), edited.ETag, Editor, Ct)));

        Assert.Empty(cleared.Categories);
    }

    [Fact]
    public async Task AnArchivedArtistCannotBeCreditedButADraftOneCan()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var retired = await host.AddArtistAsync("Retired Artist", PublishStatus.Archived);
        var upcoming = await host.AddArtistAsync("Upcoming Artist", PublishStatus.Draft);

        var refused = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ShopItemCreateViewModel("A Piece", ArtistId: retired), Editor, Ct));
        var allowed = await CreateAsync(host, new ShopItemCreateViewModel("Another Piece", ArtistId: upcoming));

        Assert.Contains("artistId", refused.Error!.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Draft, allowed.Artist!.Status);
        Assert.Equal("Upcoming Artist", allowed.Artist.Name);
    }

    [Fact]
    public async Task TheDescriptionIsStoredSanitizedWithoutItsPicturesOrLinks()
    {
        // ADR 0045: an item's pictures are its gallery, so the description keeps its words and drops the rest.
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var image = await host.AddImageAsync("The maker's bench.");

        var item = await CreateAsync(host, new ShopItemCreateViewModel(
            "Copper Cuff",
            Description: $"""<p onclick="steal()">Forged by <a href="/artists/jo">hand</a>.<script>alert(1)</script></p><img src="/media/{image}/800.webp" alt="A bench">"""));

        Assert.Equal("<p>Forged by hand.</p>", item.Description);
        Assert.Empty(item.DescriptionImageMediaIds);
        Assert.Empty((await host.StoredAsync(item.Id))!.DescriptionImages);
    }

    [Fact]
    public async Task ASkuRenameReleasesTheOldOneInTheSameSave()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var item = await CreateAsync(host, new ShopItemCreateViewModel("Copper Cuff"));

        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(Sku: "cuff.001"), item.ETag, Editor, Ct)));

        Assert.Equal("CUFF.001", renamed.Sku);
        var row = Assert.Single(await host.SkusAsync());
        Assert.Equal(("CUFF.001", item.Id), (row.Sku, row.OwnerId));
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheItemNorItsSkuReservation()
    {
        var failAudit = new FailInsertInto("AuditLogs");
        await using var host = await ShopTestHost.CreateAsync(Ct, failAudit);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.CreateAsync(
            new ShopItemCreateViewModel("Copper Cuff"), Editor, Ct)));

        Assert.Empty(await host.QueryAsync(context => context.Set<ShopItem>().AsNoTracking().ToListAsync(Ct)));
        Assert.Empty(await host.SkusAsync());
    }

    [Fact]
    public async Task AFailedRenameLeavesNeitherTheNewSlugNorItsRedirectMessage()
    {
        // outbox.md: the write and its message commit together or neither does. The create above has no message of
        // its own, so this is the path that proves it for Shop.
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ShopTestHost.CreateAsync(Ct, failAudit);
        var item = await Ready(host);
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(item.Id, item.ETag, Editor, Ct)));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.UpdateAsync(
            item.Id, new ShopItemUpdateViewModel(Slug: "the-copper-cuff"), published.ETag, Editor, Ct)));

        Assert.Equal("copper-cuff", (await host.StoredAsync(item.Id))!.Slug);

        // Scoped to the rename's own message: the publish above left a site.revalidate-requested of its own, which
        // committed with it (ADR 0036).
        Assert.Equal(0, await host.QueryAsync(context => context.OutboxMessages
            .CountAsync(message => message.Type == RedirectSlugChanged.MessageType, Ct)));
    }

    [Fact]
    public async Task AFailedStatusChangeLeavesNeitherTheStatusNorItsRevalidation()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ShopTestHost.CreateAsync(Ct, failAudit);
        var item = await Ready(host);

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.PublishAsync(
            item.Id, item.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(item.Id))!.Status);
        Assert.Empty(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task AScheduledItemIsPublishedByTheWorkerOnceAndThenSkipped()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var item = await Ready(host);
        var when = host.Time.GetUtcNow().AddHours(2);

        var scheduled = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            item.Id, new ContentScheduleViewModel(when), item.ETag, Editor, Ct)));
        var due = await host.WithFacadeAsync(facade => facade.ListDueAsync(when, 10, Ct));
        var first = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(item.Id, when, Ct));
        var second = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(item.Id, when, Ct));

        Assert.Equal((PublishStatus.Scheduled, when), (scheduled.Status, scheduled.ScheduledFor));
        Assert.Equal([item.Id], due);
        Assert.Equal(ScheduledPublishOutcome.Published, first);
        Assert.Equal(ScheduledPublishOutcome.Skipped, second);

        var stored = (await host.StoredAsync(item.Id))!;
        Assert.Equal((PublishStatus.Published, (DateTimeOffset?)null), (stored.Status, stored.ScheduledFor));
        Assert.Equal(ScheduledPublishPolicy.Actor, stored.UpdatedBy);
    }

    [Fact]
    public async Task AnArchivedItemRefusesEveryStatusCommand()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var item = await Ready(host);
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(item.Id, item.ETag, Editor, Ct)));

        var published = await host.WithFacadeAsync(facade => facade.PublishAsync(item.Id, archived.ETag, Editor, Ct));

        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, published.Error!.Code);
    }

    [Fact]
    public async Task TheDevelopmentSeedAddsTheSpikesShopOnceWithItsCategoriesAndCredits()
    {
        await using var host = await ShopTestHost.CreateAsync(Ct, withSeeders: true);

        await host.SeedAsync(Ct);
        await host.SeedAsync(Ct);

        var all = await host.QueryAsync(context => context.Set<ShopItem>().AsNoTracking()
            .Include(item => item.Categories).OrderBy(item => item.ItemNumber).ToListAsync(Ct));

        Assert.Equal(ShopDevelopmentSeeder.Pieces.Count, all.Count);
        Assert.All(all, item => Assert.Equal(PublishStatus.Published, item.Status));
        Assert.All(all, item => Assert.Equal(ShopDevelopmentSeeder.SeedActor, item.UpdatedBy));
        Assert.All(all, item => Assert.Null(item.PriceCents));
        Assert.All(all, item => Assert.NotNull(item.Description));
        Assert.All(all, item => Assert.Single(item.Categories));

        var cuff = all.Single(item => item.Title == "Copper Cuff");
        Assert.True(cuff.OneOfAKind);
        Assert.Equal(1, cuff.StartingStockQuantity);
        Assert.Equal(11000, cuff.StartingPriceCents);
        Assert.NotNull(cuff.ArtistId);

        // The spike's magazine credits nobody, and the print is an edition rather than one of a kind.
        Assert.Null(all.Single(item => item.Title == "Chicago Collage Magazine Vol. 1").ArtistId);
        Assert.False(all.Single(item => item.Title == "The Guide (print)").OneOfAKind);

        Assert.Equal(all.Count, (await host.SkusAsync()).Count);
        Assert.Single(await host.AuditActionsAsync(), action => action == "shop-item.seeded");
    }

    [Fact]
    public async Task ADeletedRecordIsGoneFromEveryReadAndARestoreBringsItBackAsADraft()
    {
        // ADR 0052: one query filter, so "deleted" means gone from every read — and a restore is a decision to
        // look at the record again, not to publish it.
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var created = await CreateAsync(host, new ShopItemCreateViewModel("Copper Cuff"));

        var deleted = await host.WithFacadeAsync(facade => facade.DeleteAsync(created.Id, created.ETag, ShopTestHost.AdminId, Ct));

        Assert.True(deleted.Succeeded, deleted.Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, (await host.WithFacadeAsync(facade => facade.GetAsync(created.Id, Ct))).Error!.Code);
        Assert.Empty((await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ShopItemListViewModel(), Ct)))).Items);
        Assert.Null(await host.StoredAsync(created.Id));

        // The address is free at once, which is the point: the mistake stops standing in the way.
        var replacement = await CreateAsync(host, new ShopItemCreateViewModel("Copper Cuff"));
        Assert.Equal("copper-cuff", replacement.Slug);

        var trash = await Ok(host.WithFacadeAsync(facade => facade.ListDeletedAsync(new ShopItemListViewModel(), Ct)));
        var row = Assert.Single(trash.Items);
        Assert.Equal("Copper Cuff", row.Title);

        // The replacement holds the address now, so the restore is refused until it does not.
        var refused = await host.WithFacadeAsync(facade => facade.RestoreAsync(created.Id, row.ETag, ShopTestHost.AdminId, Ct));
        Assert.Equal(ContentErrorCodes.SlugTaken, refused.Error!.Code);

        await host.WithFacadeAsync(facade => facade.DeleteAsync(replacement.Id, replacement.ETag, ShopTestHost.AdminId, Ct));
        var restored = await Ok(host.WithFacadeAsync(facade => facade.RestoreAsync(created.Id, row.ETag, ShopTestHost.AdminId, Ct)));

        Assert.Equal(PublishStatus.Draft, restored.Status);
        Assert.Null(restored.ScheduledFor);
        Assert.Contains("shop-item.deleted", await host.AuditActionsAsync());
        Assert.Contains("shop-item.restored", await host.AuditActionsAsync());
    }

    /// <summary>A draft with everything publishing requires: copy, a price, a count and a described photograph.</summary>
    private static async Task<ShopItemServiceModel> Ready(ShopTestHost host)
    {
        var photograph = await host.AddImageAsync("The cuff from the front.");
        return await CreateAsync(host, new ShopItemCreateViewModel(
            "Copper Cuff",
            Description: "<p>A forged copper cuff.</p>",
            Categories: ["jewelry"],
            ImageMediaIds: [photograph],
            OneOfAKind: true,
            StartingPriceCents: 11000));
    }

    [Fact]
    public async Task DeletingReleasesTheSkuAndRestoringTakesItBackOrSaysWhoHasIt()
    {
        // ADR 0037, 0052: the SKU is the catalog's unique key, and a deleted piece holds nothing. The piece that
        // replaces it may take the SKU, and then the restore has to say so rather than break the unique key.
        await using var host = await ShopTestHost.CreateAsync(Ct);
        var first = await CreateAsync(host, new ShopItemCreateViewModel("Copper Cuff"));

        Assert.Equal([first.Sku], await SkusAsync(host));

        await host.WithFacadeAsync(facade => facade.DeleteAsync(first.Id, first.ETag, Admin, Ct));
        Assert.Empty(await SkusAsync(host));

        // Free, so the replacement is offered and takes the same SKU.
        var second = await CreateAsync(host, new ShopItemCreateViewModel("Copper Cuff II", Sku: first.Sku));
        Assert.Equal([first.Sku], await SkusAsync(host));

        var tag = EntityTag.Format(await host.QueryAsync(context => context.Set<ShopItem>()
            .IgnoreQueryFilters().AsNoTracking().Where(row => row.Id == first.Id)
            .Select(row => row.RowVersion).SingleAsync(Ct)));
        var refused = await host.WithFacadeAsync(facade => facade.RestoreAsync(first.Id, tag, Admin, Ct));

        Assert.Equal(CatalogErrorCodes.SkuTaken, refused.Error!.Code);
        Assert.Contains(first.Sku, refused.Error.FieldErrors["sku"][0], StringComparison.Ordinal);

        // With the replacement gone the SKU is free again, and the restore takes it back.
        await host.WithFacadeAsync(facade => facade.DeleteAsync(second.Id, second.ETag, Admin, Ct));
        var restored = await Ok(host.WithFacadeAsync(facade => facade.RestoreAsync(first.Id, tag, Admin, Ct)));

        Assert.Equal(first.Sku, restored.Sku);
        Assert.Equal([first.Sku], await SkusAsync(host));
        Assert.Equal(
            [new SkuOwner(CatalogOwnerTypes.ShopItem, first.Id)],
            await host.QueryAsync(context => context.Set<CatalogSku>().AsNoTracking()
                .Select(row => new SkuOwner(row.OwnerType, row.OwnerId)).ToListAsync(Ct)));
    }

    /// <summary>Every SKU the registry holds: what a delete releases and a restore takes back.</summary>
    private static Task<List<string>> SkusAsync(ShopTestHost host) =>
        host.QueryAsync(context => context.Set<CatalogSku>().AsNoTracking()
            .OrderBy(row => row.Sku).Select(row => row.Sku).ToListAsync(Ct));

    private static async Task<ShopItemServiceModel> CreateAsync(ShopTestHost host, ShopItemCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    /// <summary>What the 5.x inbound pull writes: the projections, and only those (square.md).</summary>
    private static Task SquareReports(ShopTestHost host, Guid itemId, long priceCents, int stockOnHand) =>
        host.ExecuteAsync(async context =>
        {
            var item = await context.Set<ShopItem>().SingleAsync(row => row.Id == itemId, Ct);
            item.PriceCents = priceCents;
            item.StockOnHand = stockOnHand;
            item.SoldOut = stockOnHand == 0;
            await context.SaveChangesAsync(Ct);
        });

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }
}
