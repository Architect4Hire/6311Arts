using System.Reflection;
using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Revalidation;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Facade;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.ServiceModels;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;
using Arts6311.Domain.Modules.Site.Facade;
using Arts6311.Domain.Modules.Site.Managers.ServiceModels;
using Arts6311.Tests.Site.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Site;

/// <summary>
/// The site's read API end to end through each facade, on a real (SQLite) database and a real HybridCache (ADR 0042):
/// what the public sees and in what order, what it never sees, and how long it sees it — a miss reads the database, a
/// hit does not, and a committed change reaches the cache through the outbox and the revalidation handler.
/// </summary>
public sealed class SiteReadTests
{
    private const string Editor = SiteTestHost.EditorId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- Caching: miss, hit, invalidate ----

    [Fact]
    public async Task APageIsReadOnceThenServedFromTheCacheUntilItsEditIsRevalidated()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var id = await host.AddPageAsync("Privacy");

        // A miss reads the database.
        Assert.Equal("Privacy", (await PageAsync(host, "privacy")).Title);

        // A staff edit commits, and stages its revalidation in the same save.
        var etag = (await Ok(host.WithAsync<IPagesFacade, OperationResult<PageServiceModel>>(
            facade => facade.GetAsync(id, Ct)))).ETag;
        await Ok(host.WithAsync<IPagesFacade, OperationResult<PageServiceModel>>(
            facade => facade.UpdateAsync(id, new PageUpdateViewModel(Title: "Privacy policy"), etag, Editor, Ct)));

        // A hit does not: until the Worker delivers the message, the site keeps the answer it had.
        Assert.Equal("Privacy", (await PageAsync(host, "privacy")).Title);

        await host.DispatchAsync(Ct);

        Assert.Equal("Privacy policy", (await PageAsync(host, "privacy")).Title);
    }

    [Fact]
    public async Task ARenamedArtistReachesEveryCachedReadThatCreditsThem()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var artist = await host.AddArtistAsync("Jane Doe");
        await host.AddExhibitionAsync("Spring Show", new DateOnly(2026, 3, 1), artists: [artist]);
        await host.AddShopItemAsync("Copper Cuff", artistId: artist);

        Assert.Equal("Jane Doe", (await ExhibitionsAsync(host))[0].Artists[0].Name);
        Assert.Equal("Jane Doe", (await ShopAsync(host)).Items[0].Artist!.Name);
        Assert.Equal("Jane Doe", (await host.WithAsync<ISiteFacade, OperationResult<IReadOnlyList<SiteArtistSummaryServiceModel>>>(
            facade => facade.ListArtistsAsync(Ct))).Value![0].Name);

        var etag = (await Ok(host.WithAsync<IArtistsFacade, OperationResult<ArtistServiceModel>>(
            facade => facade.GetAsync(artist, Ct)))).ETag;
        await Ok(host.WithAsync<IArtistsFacade, OperationResult<ArtistServiceModel>>(
            facade => facade.UpdateAsync(artist, new ArtistUpdateViewModel(Name: "Jane Q. Doe"), etag, Editor, Ct)));
        await host.DispatchAsync(Ct);

        // The artist's own type tag is on the exhibition, shop and directory entries, so one message clears all three.
        Assert.Equal("Jane Q. Doe", (await ExhibitionsAsync(host))[0].Artists[0].Name);
        Assert.Equal("Jane Q. Doe", (await ShopAsync(host)).Items[0].Artist!.Name);
        Assert.Equal("Jane Q. Doe", (await host.WithAsync<ISiteFacade, OperationResult<IReadOnlyList<SiteArtistSummaryServiceModel>>>(
            facade => facade.ListArtistsAsync(Ct))).Value![0].Name);
    }

    [Fact]
    public async Task EachSiteReadIsClearedByItsOwnTypesRevalidation()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        await host.AddClassAsync("Etching", runs: [(host.Now.AddDays(3), 40_00, 5, false)]);
        await host.AddPlanAsync("Friend", 1);
        await host.AddShopItemAsync("Copper Cuff");

        // Warm every list, then change the rows behind the cache's back: no outbox message, so nothing tells it.
        Assert.Single(await ClassesAsync(host));
        Assert.Single(await PlansAsync(host));
        Assert.Single((await ShopAsync(host)).Items);
        Assert.Single(await SitemapAsync(host), entry => entry.Path == "/classes/etching");

        await host.AddClassAsync("Woodcut", runs: [(host.Now.AddDays(4), 40_00, 5, false)]);
        await host.AddPlanAsync("Patron", 2);
        await host.AddShopItemAsync("Silver Ring");

        Assert.Single(await ClassesAsync(host));
        Assert.Single(await PlansAsync(host));
        Assert.Single((await ShopAsync(host)).Items);

        // The kernel handler, as the Worker would run it for each type's message (ADR 0036).
        foreach (var type in new[] { ClassAudit.ResourceType, MembershipAudit.ResourceType, ShopAudit.ResourceType })
        {
            await host.WithAsync<ISiteRevalidationFacade, bool>(async facade =>
            {
                await facade.RevalidateAsync(new SiteRevalidateRequested(type, Guid.NewGuid(), []), Ct);
                return true;
            });
        }

        Assert.Equal(2, (await ClassesAsync(host)).Count);
        Assert.Equal(2, (await PlansAsync(host)).Count);
        Assert.Equal(2, (await ShopAsync(host)).Items.Count);
        Assert.Contains(await SitemapAsync(host), entry => entry.Path == "/classes/woodcut");
    }

    [Fact]
    public async Task ANotFoundIsNotCachedSoAPublishShowsAtOnce()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var id = await host.AddPageAsync("Terms", PublishStatus.Draft);

        var draft = await host.WithAsync<IPagesFacade, OperationResult<SitePageServiceModel>>(
            facade => facade.GetForSiteAsync("terms", Ct));
        Assert.Equal(ErrorCodes.NotFound, draft.Error!.Code);

        await host.ExecuteAsync(context => context.Set<Page>().Where(page => page.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(page => page.Status, PublishStatus.Published), Ct));

        Assert.Equal("Terms", (await PageAsync(host, "terms")).Title);
    }

    [Fact]
    public async Task OnlyAShopsFirstPageIsCached()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        for (var i = 0; i < 3; i++)
        {
            await host.AddShopItemAsync($"Piece {i}", publishedAt: host.Now.AddMinutes(-i));
        }

        var first = await ShopAsync(host, new SiteShopListViewModel(Limit: 2));
        await host.AddShopItemAsync("Piece 3", publishedAt: host.Now.AddMinutes(-10));

        // The first page is the cached one; a later page, keyed by the visitor's cursor, is read each time.
        Assert.Equal(first.Items.Select(item => item.Title), (await ShopAsync(host, new SiteShopListViewModel(Limit: 2))).Items.Select(item => item.Title));
        Assert.Equal(["Piece 2", "Piece 3"], (await ShopAsync(host, new SiteShopListViewModel(Cursor: first.NextCursor, Limit: 2))).Items.Select(item => item.Title));
    }

    // ---- Pages ----

    [Fact]
    public async Task OnlyAPublishedPageIsThereAndAMalformedSlugNamesNothing()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var id = await host.AddPageAsync("Privacy");
        await host.AddPageAsync("Terms", PublishStatus.Draft);
        await host.AddPageAsync("Old Policy", PublishStatus.Archived);

        var page = await PageAsync(host, "privacy");
        Assert.Equal((id, "privacy", "Privacy", "<p>Text.</p>"), (page.Id, page.Slug, page.Title, page.BodyHtml));

        foreach (var slug in new[] { "terms", "old-policy", "nobody", "Privacy", "a:b", string.Empty })
        {
            var missing = await host.WithAsync<IPagesFacade, OperationResult<SitePageServiceModel>>(
                facade => facade.GetForSiteAsync(slug, Ct));
            Assert.Equal(ErrorCodes.NotFound, missing.Error!.Code);
        }
    }

    // ---- Classes ----

    [Fact]
    public async Task TheClassListShowsWhatIsStillToComeSoonestFirst()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var now = host.Now;
        await host.AddClassAsync("Later", runs: [(now.AddDays(10), 50_00, 4, false)]);
        await host.AddClassAsync(
            "Running Course",
            runs: [(now.AddDays(-7), 40_00, 0, true), (now.AddDays(2), 45_00, 3, false), (now.AddDays(9), null, null, false)]);
        await host.AddClassAsync("In Progress", runs: [(now.AddHours(-1), null, null, false)]);
        await host.AddClassAsync("Over", runs: [(now.AddDays(-3), 40_00, 0, false)]);
        await host.AddClassAsync("Draft", PublishStatus.Draft, runs: [(now.AddDays(1), 40_00, 4, false)]);

        var classes = await ClassesAsync(host);

        Assert.Equal(["In Progress", "Running Course", "Later"], classes.Select(offering => offering.Title));

        // A class shows only the runs left, soonest first, and is "from" the lowest price Square has given them.
        var course = classes[1];
        Assert.Equal([now.AddDays(2), now.AddDays(9)], course.Runs.Select(run => run.StartsAt));
        Assert.Equal(45_00, course.FromPriceCents);
        var next = course.Runs[0];
        Assert.Equal(("USD", "America/Chicago", "Saturdays"), (course.Currency, next.DisplayTimeZone, next.Cadence));
        Assert.Equal((10, 3, false), (next.Capacity, next.SeatsRemaining!.Value, next.SoldOut));
        Assert.Equal((1, now.AddDays(2).AddHours(2)), (next.SessionCount, next.EndsAt));
        Assert.Null(next.Instructor);

        // A run in progress is still shown; one Square has not priced yet has no price.
        Assert.Null(classes[0].FromPriceCents);
    }

    [Fact]
    public async Task ARunNamesItsInstructorAndLinksThemOnlyOnceTheirPageIsPublished()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        var drafting = await host.AddInstructorAsync("New Teacher", PublishStatus.Draft);
        await host.AddClassAsync("Intro", gwen, runs: [(host.Now.AddDays(3), null, null, false)]);
        await host.AddClassAsync("Etching", drafting, runs: [(host.Now.AddDays(4), null, null, false)]);

        var classes = await ClassesAsync(host);

        Assert.Equal(("Gwen Schilling", "gwen-schilling"), (classes[0].Runs[0].Instructor!.Name, classes[0].Runs[0].Instructor!.Slug));
        Assert.Equal(("New Teacher", (string?)null), (classes[1].Runs[0].Instructor!.Name, classes[1].Runs[0].Instructor!.Slug));
    }

    [Fact]
    public async Task TheListNarrowsToOnePublishedInstructorsRunsAndAnyOtherSlugIsEmpty()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        var nick = await host.AddInstructorAsync("Nicholas Waguespack");
        var drafting = await host.AddInstructorAsync("New Teacher", PublishStatus.Draft);
        await host.AddClassAsync("Intro", gwen, runs: [(host.Now.AddDays(3), null, null, false)]);
        await host.AddClassAsync("Playground", nick, runs: [(host.Now.AddDays(4), null, null, false)]);
        await host.AddClassAsync("Etching", drafting, runs: [(host.Now.AddDays(5), null, null, false)]);

        var hers = await ClassesAsync(host, "gwen-schilling");
        var draft = await ClassesAsync(host, "new-teacher");
        var nobody = await ClassesAsync(host, "nobody");
        var malformed = await ClassesAsync(host, "Not:A Slug");

        Assert.Equal(["Intro"], hers.Select(offering => offering.Title));
        Assert.Empty(draft);
        Assert.Empty(nobody);
        Assert.Empty(malformed);
    }

    [Fact]
    public async Task AClassThatIsOverKeepsItsPageWithNoSessionsAndADraftHasNone()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var hero = await host.AddImageAsync("A press mid-print.");
        await host.AddClassAsync("Over", hero: hero, runs: [(host.Now.AddDays(-3), 40_00, 0, false)]);
        await host.AddClassAsync("Draft", PublishStatus.Draft, runs: [(host.Now.AddDays(1), 40_00, 4, false)]);

        var over = await Ok(host.WithAsync<IClassesFacade, OperationResult<SiteClassServiceModel>>(
            facade => facade.GetForSiteAsync("over", Ct)));
        var draft = await host.WithAsync<IClassesFacade, OperationResult<SiteClassServiceModel>>(
            facade => facade.GetForSiteAsync("draft", Ct));

        Assert.Empty(over.Runs);
        Assert.Equal("<p>What you will make.</p>", over.BodyHtml);
        Assert.Equal("A press mid-print.", over.Image!.Alt);
        Assert.Equal(ErrorCodes.NotFound, draft.Error!.Code);
    }

    // ---- Exhibitions ----

    [Fact]
    public async Task ExhibitionsAreNewestFirstAndAnUnpublishedArtistIsCreditedWithoutALink()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var ann = await host.AddArtistAsync("Ann Arbor");
        var bob = await host.AddArtistAsync("Bob Draft", PublishStatus.Draft);
        await host.AddExhibitionAsync("Spring Show", new DateOnly(2026, 3, 1), artists: [bob, ann]);
        await host.AddExhibitionAsync("Autumn Show", new DateOnly(2026, 9, 1));
        await host.AddExhibitionAsync("Hidden Show", new DateOnly(2026, 10, 1), PublishStatus.Draft);

        var exhibitions = await ExhibitionsAsync(host);

        Assert.Equal(["Autumn Show", "Spring Show"], exhibitions.Select(exhibition => exhibition.Title));

        // In the order staff set; the draft artist by name only, because the site has no page to link to.
        Assert.Equal([("Bob Draft", (string?)null), ("Ann Arbor", "ann-arbor")], exhibitions[1].Artists.Select(credit => (credit.Name, credit.Slug)));
        Assert.Equal(("America/Chicago", "Light refreshments"), (exhibitions[1].DisplayTimeZone, exhibitions[1].Reception!.Note));

        var spring = await Ok(host.WithAsync<IExhibitionsFacade, OperationResult<SiteExhibitionServiceModel>>(
            facade => facade.GetForSiteAsync("spring-show", Ct)));
        Assert.Equal("First paragraph.\n\nSecond paragraph.", spring.BodyText);
    }

    // ---- Artists ----

    [Fact]
    public async Task TheDirectoryCountsEachArtistsPublishedWorkAndShowsOnlyPublishedArtists()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync("Jane at the press.");
        var jane = await host.AddArtistAsync("Jane Doe", portrait: portrait);
        await host.AddArtistAsync("Amy Active", active: false);
        await host.AddArtistAsync("Zed Draft", PublishStatus.Draft);
        await host.AddShopItemAsync("Copper Cuff", artistId: jane);
        await host.AddShopItemAsync("Silver Ring", artistId: jane);
        await host.AddShopItemAsync("Unfinished", PublishStatus.Draft, artistId: jane);

        var directory = (await host.WithAsync<ISiteFacade, OperationResult<IReadOnlyList<SiteArtistSummaryServiceModel>>>(
            facade => facade.ListArtistsAsync(Ct))).Value!;

        Assert.Equal([("Amy Active", false, 0), ("Jane Doe", true, 2)], directory.Select(artist => (artist.Name, artist.Active, artist.WorkCount)));
        Assert.Equal("Jane at the press.", directory[1].Portrait!.Alt);
    }

    [Fact]
    public async Task AnArtistsPageCarriesTheirPublishedWorkAndADraftArtistHasNoPage()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var jane = await host.AddArtistAsync("Jane Doe", bio: "Prints.\n\nAnd more prints.");
        await host.AddArtistAsync("Zed Draft", PublishStatus.Draft);
        await host.AddShopItemAsync("Copper Cuff", artistId: jane);
        await host.AddShopItemAsync("Unfinished", PublishStatus.Draft, artistId: jane);
        await host.AddShopItemAsync("Someone Else's");

        var page = await Ok(host.WithAsync<ISiteFacade, OperationResult<SiteArtistPageServiceModel>>(
            facade => facade.GetArtistAsync("jane-doe", Ct)));
        var draft = await host.WithAsync<ISiteFacade, OperationResult<SiteArtistPageServiceModel>>(
            facade => facade.GetArtistAsync("zed-draft", Ct));

        Assert.Equal(("Jane Doe", "Prints.\n\nAnd more prints."), (page.Artist.Name, page.Artist.BioText));
        Assert.Equal(["Copper Cuff"], page.Works.Items.Select(item => item.Title));
        Assert.Null(page.Works.NextCursor);
        Assert.Equal(ErrorCodes.NotFound, draft.Error!.Code);
    }

    // ---- Shop ----

    [Theory]
    [InlineData(null)]
    [InlineData("newest")]
    [InlineData("price-asc")]
    [InlineData("price-desc")]
    [InlineData("title")]
    public async Task EveryOrderPagesThroughEveryPublishedPieceOnceWithUnpricedLast(string? sort)
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var now = host.Now;
        var pieces = new (string Title, long? Price, int AgeMinutes)[]
        {
            ("Bowl", 40_00, 5), ("Cuff", 125_00, 1), ("Etching", 40_00, 3), ("Print", null, 2), ("Ring", 90_00, 4), ("Vase", null, 6),
        };
        foreach (var (title, price, age) in pieces)
        {
            await host.AddShopItemAsync(title, priceCents: price, publishedAt: now.AddMinutes(-age));
        }

        await host.AddShopItemAsync("Hidden", PublishStatus.Draft);

        var seen = new List<SiteShopItemSummaryServiceModel>();
        string? cursor = null;
        do
        {
            var page = await ShopAsync(host, new SiteShopListViewModel(Sort: sort, Cursor: cursor, Limit: 2));
            Assert.InRange(page.Items.Count, 1, 2);
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        string[] expected = sort switch
        {
            "price-asc" => ["Bowl", "Etching", "Ring", "Cuff", "Print", "Vase"],
            "price-desc" => ["Cuff", "Ring", "Bowl", "Etching", "Print", "Vase"],
            "title" => ["Bowl", "Cuff", "Etching", "Print", "Ring", "Vase"],
            _ => ["Cuff", "Print", "Etching", "Ring", "Bowl", "Vase"],
        };

        // Equal prices and unpriced pieces tie-break on the id, so compare the two groups as sets where they tie.
        Assert.Equal(expected.Length, seen.Select(item => item.Id).Distinct().Count());
        if (sort is "price-asc" or "price-desc")
        {
            Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Select(item => item.Title).Order(StringComparer.Ordinal));
            Assert.Equal(seen.Select(item => item.PriceCents), expected.Select(title => pieces.Single(piece => piece.Title == title).Price));
            Assert.Equal([null, null], seen.TakeLast(2).Select(item => item.PriceCents));
        }
        else
        {
            Assert.Equal(expected, seen.Select(item => item.Title));
        }
    }

    [Fact]
    public async Task TheShopFiltersByCategoryAndByAPublishedArtist()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var jane = await host.AddArtistAsync("Jane Doe");
        var zed = await host.AddArtistAsync("Zed Draft", PublishStatus.Draft);
        await host.AddShopItemAsync("Copper Cuff", artistId: jane, categories: ["jewelry"]);
        await host.AddShopItemAsync("Etching", artistId: jane, categories: ["prints"]);
        await host.AddShopItemAsync("Bowl", artistId: zed, categories: ["ceramics"]);

        Assert.Equal(["Copper Cuff"], (await ShopAsync(host, new SiteShopListViewModel(Category: "jewelry"))).Items.Select(item => item.Title));
        Assert.Equal(["Copper Cuff", "Etching"], (await ShopAsync(host, new SiteShopListViewModel(Artist: "jane-doe", Sort: "title"))).Items.Select(item => item.Title));

        // A draft artist's work is still in the shop, credited by name; filtering by their slug finds nothing.
        Assert.Empty((await ShopAsync(host, new SiteShopListViewModel(Artist: "zed-draft"))).Items);
        Assert.Empty((await ShopAsync(host, new SiteShopListViewModel(Artist: "nobody"))).Items);
        var bowl = (await ShopAsync(host, new SiteShopListViewModel(Category: "ceramics"))).Items.Single();
        Assert.Equal(("Zed Draft", (string?)null), (bowl.Artist!.Name, bowl.Artist.Slug));
    }

    [Fact]
    public async Task TheShopsTotalCountsWhatItsFiltersMatchAndReadsTheSameOnEveryPage()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var jane = await host.AddArtistAsync("Jane Doe");
        var zed = await host.AddArtistAsync("Zed Draft", PublishStatus.Draft);
        await host.AddShopItemAsync("Copper Cuff", artistId: jane, categories: ["jewelry"]);
        await host.AddShopItemAsync("Etching", artistId: jane, categories: ["prints"]);
        await host.AddShopItemAsync("Linocut", categories: ["prints"]);
        await host.AddShopItemAsync("Bowl", artistId: zed, categories: ["ceramics"]);
        await host.AddShopItemAsync("Unfinished", PublishStatus.Draft, categories: ["prints"]);

        Assert.Equal(4, (await ShopAsync(host)).TotalCount);
        Assert.Equal(2, (await ShopAsync(host, new SiteShopListViewModel(Category: "prints"))).TotalCount);
        Assert.Equal(2, (await ShopAsync(host, new SiteShopListViewModel(Artist: "jane-doe"))).TotalCount);
        Assert.Equal(1, (await ShopAsync(host, new SiteShopListViewModel(Category: "prints", Artist: "jane-doe"))).TotalCount);

        // An artist with nothing to show is an empty shop, and says so.
        Assert.Equal(0, (await ShopAsync(host, new SiteShopListViewModel(Artist: "zed-draft"))).TotalCount);

        // The count is the list's, not the page's: a later page, read past the cache, gives the same number.
        var first = await ShopAsync(host, new SiteShopListViewModel(Limit: 3));
        var second = await ShopAsync(host, new SiteShopListViewModel(Cursor: first.NextCursor, Limit: 3));
        Assert.Equal((3, 4), (first.Items.Count, first.TotalCount));
        Assert.Equal((1, 4, (string?)null), (second.Items.Count, second.TotalCount, second.NextCursor));
    }

    [Fact]
    public async Task ACursorFromAnotherListOrOrderIsRefusedAndABadQueryIsAValidationFailure()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        for (var i = 0; i < 3; i++)
        {
            await host.AddShopItemAsync($"Piece {i}", categories: ["prints"]);
        }

        var first = await ShopAsync(host, new SiteShopListViewModel(Limit: 1));

        foreach (var request in new[]
        {
            new SiteShopListViewModel(Sort: "title", Cursor: first.NextCursor, Limit: 1),
            new SiteShopListViewModel(Category: "prints", Cursor: first.NextCursor, Limit: 1),
            new SiteShopListViewModel(Cursor: "not-a-cursor"),
        })
        {
            var refused = await host.WithAsync<IShopFacade, OperationResult<CountedCursorPage<SiteShopItemSummaryServiceModel>>>(
                facade => facade.ListForSiteAsync(request, Ct));
            Assert.Equal(ErrorCodes.CursorInvalid, refused.Error!.Code);
        }

        foreach (var request in new[]
        {
            new SiteShopListViewModel(Sort: "cheapest"),
            new SiteShopListViewModel(Category: "merch"),
            new SiteShopListViewModel(Artist: "Not A Slug"),
            new SiteShopListViewModel(Limit: 0),
            new SiteShopListViewModel(Cursor: new string('a', ShopPolicy.SiteCursorMaxLength + 1)),
        })
        {
            var refused = await host.WithAsync<IShopFacade, OperationResult<CountedCursorPage<SiteShopItemSummaryServiceModel>>>(
                facade => facade.ListForSiteAsync(request, Ct));
            Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        }
    }

    [Fact]
    public async Task APiecesPageShowsItsGalleryInOrderAsRenditionsOnly()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var front = await host.AddImageAsync("The cuff from the front.");
        var pending = await host.AddImageAsync("Not rendered yet.", withRenditions: false);
        var side = await host.AddImageAsync("The cuff from the side.");
        await host.AddShopItemAsync("Copper Cuff", images: [front, pending, side]);

        var piece = await Ok(host.WithAsync<IShopFacade, OperationResult<SiteShopItemServiceModel>>(facade => facade.GetForSiteAsync("copper-cuff", Ct)));
        var card = (await ShopAsync(host)).Items.Single();

        // A photograph with no rendition yet is left out rather than shown broken; the master is never offered.
        Assert.Equal(["The cuff from the front.", "The cuff from the side."], piece.Images.Select(image => image.Alt));
        Assert.Equal(piece.Images[0].Src, piece.Image!.Src);
        Assert.Equal(piece.Image.Src, card.Image!.Src);
        Assert.StartsWith($"/media/{front}/1600.jpg?v=", piece.Image.Src, StringComparison.Ordinal);
        Assert.Equal((1600, 1000), (piece.Image.Width, piece.Image.Height));
        Assert.Equal(6, piece.Image.Sources.Count);
        Assert.DoesNotContain(piece.Image.Sources, source => source.Url.Contains("/original", StringComparison.Ordinal));
        Assert.Equal(("<p>A piece.</p>", 125_00L, "USD", true, false), (piece.DescriptionHtml, piece.PriceCents!.Value, piece.Currency, piece.OneOfAKind, piece.SoldOut));
    }

    // ---- Memberships ----

    [Fact]
    public async Task TiersAreInDisplayOrderPricedBySquareNotByTheirStartingFigures()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        await host.AddPlanAsync("Patron", 2);
        await host.AddPlanAsync("Friend", 1, priceCents: null, cadence: null);
        await host.AddPlanAsync("Founder", 3, PublishStatus.Draft);

        var plans = await PlansAsync(host);

        Assert.Equal(["Friend", "Patron"], plans.Select(plan => plan.Name));
        Assert.Equal(((long?)null, (string?)null), (plans[0].PriceCents, plans[0].Cadence));
        Assert.Equal((60_00L, "ANNUAL", "USD"), (plans[1].PriceCents!.Value, plans[1].Cadence!, plans[1].Currency));
        Assert.Equal(["Class discount"], plans[1].Perks);
    }

    // ---- Sitemap ----

    [Fact]
    public async Task TheSitemapListsEveryPublishedAddressOnceAndNothingElse()
    {
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var artist = await host.AddArtistAsync("Jane Doe");
        await host.AddArtistAsync("Zed Draft", PublishStatus.Draft);
        await host.AddPageAsync("Privacy");
        await host.AddPageAsync("Terms", PublishStatus.Draft);
        await host.AddClassAsync("Etching", runs: [(host.Now.AddDays(-30), 40_00, 0, false)]);
        await host.AddExhibitionAsync("Spring Show", new DateOnly(2026, 3, 1), artists: [artist]);
        await host.AddShopItemAsync("Copper Cuff");
        await host.AddShopItemAsync("Unfinished", PublishStatus.Draft);
        await host.AddPlanAsync("Friend", 1);
        await host.AddPlanAsync("Patron", 2);

        var sitemap = await SitemapAsync(host);

        // A class that is over keeps its page, so it stays in the sitemap; every tier shares one address.
        Assert.Equal(
            ["/artists/jane-doe", "/classes/etching", "/exhibitions/spring-show", "/memberships", "/privacy", "/shop/copper-cuff"],
            sitemap.Select(entry => entry.Path).Order(StringComparer.Ordinal));
        Assert.All(sitemap, entry => Assert.Equal(host.Now, entry.LastModified));
    }

    // ---- What the site never sees ----

    [Fact]
    public void NoSiteModelCarriesAStaffFieldAStatusOrASquareId()
    {
        // ADR 0042: the site's models are built for the public, so a field the CMS keeps for staff (who wrote it, the
        // row version, the legacy id, the SKU, the starting figures, the stock count) cannot appear on any of them.
        string[] forbidden =
        [
            "UpdatedBy", "ETag", "RowVersion", "Status", "LegacyWordPressId", "Sku", "StockOnHand", "StartingPriceCents",
            "StartingStockQuantity", "StartingCadence", "ScheduledFor", "PublishedAt", "CreatedAt", "ItemNumber",
            "OfferingNumber", "SquareHasRecord", "SlugLocked", "OriginalUrl", "Visibility",
        ];
        var models = typeof(SitePaths).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Name.StartsWith("Site", StringComparison.Ordinal)
                && type.Name.EndsWith("ServiceModel", StringComparison.Ordinal))
            .Append(typeof(SitemapEntryServiceModel))
            .ToList();

        Assert.True(models.Count >= 17, $"Only {models.Count} site models were found; the scan has gone wrong.");
        foreach (var model in models)
        {
            var names = model.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ToList();
            Assert.Empty(names.Intersect(forbidden, StringComparer.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, name => name.Contains("Square", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task ARenamedDraftArtistReachesThePublishedWorkThatCreditsThem()
    {
        // ADR 0042: credited by name though their own page is not published, so their edit has to revalidate too.
        await using var host = await SiteTestHost.CreateAsync(Ct);
        var artist = await host.AddArtistAsync("Zed Draft", PublishStatus.Draft);
        await host.AddShopItemAsync("Bowl", artistId: artist);
        Assert.Equal("Zed Draft", (await ShopAsync(host)).Items[0].Artist!.Name);

        var etag = (await Ok(host.WithAsync<IArtistsFacade, OperationResult<ArtistServiceModel>>(facade => facade.GetAsync(artist, Ct)))).ETag;
        await Ok(host.WithAsync<IArtistsFacade, OperationResult<ArtistServiceModel>>(
            facade => facade.UpdateAsync(artist, new ArtistUpdateViewModel(Name: "Zed Potter"), etag, Editor, Ct)));
        await host.DispatchAsync(Ct);

        Assert.Equal(("Zed Potter", (string?)null), ((await ShopAsync(host)).Items[0].Artist!.Name, (await ShopAsync(host)).Items[0].Artist!.Slug));
    }

    [Fact]
    public void EverySiteReadIsTaggedWithEveryTypeItShows()
    {
        // The tag contract, pinned (ADR 0042): a read that shows another type's records carries that type's tag, or
        // that type's change leaves it stale until it expires.
        static string[] Types(params string[] types) => [.. types.Select(CacheTags.Type)];

        Assert.Equal(Types(SiteContentTypes.Page), PagesFacade.SiteTags);
        Assert.Equal(Types(SiteContentTypes.ClassOffering, SiteContentTypes.Instructor), ClassesFacade.SiteTags);
        Assert.Equal(Types(SiteContentTypes.Instructor, SiteContentTypes.Artist), InstructorsFacade.SiteTags);
        Assert.Equal(Types(SiteContentTypes.MembershipPlan), MembershipsFacade.SiteTags);
        Assert.Equal(Types(SiteContentTypes.Exhibition, SiteContentTypes.Artist), ExhibitionsFacade.SiteTags);
        Assert.Equal(Types(SiteContentTypes.ShopItem, SiteContentTypes.Artist), ShopFacade.SiteTags);
        Assert.Equal(Types(SiteContentTypes.Artist, SiteContentTypes.ShopItem), SiteFacade.ArtistTags);
        Assert.Equal(Types([.. SiteContentTypes.All]), SiteFacade.SitemapTags);
    }

    [Fact]
    public void TheKernelsContentTypeNamesAreEachModulesOwn()
    {
        // The cross-type tags are spelled in the kernel (SiteContentTypes); the revalidation each module stages is
        // spelled by its own audit constant. A drift would leave an embedded credit stale until the cache expires.
        Assert.Equal(
            [ArtistAudit.ResourceType, InstructorAudit.ResourceType, ExhibitionAudit.ResourceType, ClassAudit.ResourceType, ShopAudit.ResourceType, MembershipAudit.ResourceType, PageAudit.ResourceType],
            SiteContentTypes.All);
    }

    // ---- Helpers ----

    private static async Task<SitePageServiceModel> PageAsync(SiteTestHost host, string slug) =>
        await Ok(host.WithAsync<IPagesFacade, OperationResult<SitePageServiceModel>>(
            facade => facade.GetForSiteAsync(slug, Ct)));

    private static async Task<IReadOnlyList<SiteClassSummaryServiceModel>> ClassesAsync(SiteTestHost host, string? instructor = null) =>
        await Ok(host.WithAsync<IClassesFacade, OperationResult<IReadOnlyList<SiteClassSummaryServiceModel>>>(
            facade => facade.ListForSiteAsync(new SiteClassListViewModel(instructor), Ct)));

    private static async Task<IReadOnlyList<SiteExhibitionSummaryServiceModel>> ExhibitionsAsync(SiteTestHost host) =>
        await Ok(host.WithAsync<IExhibitionsFacade, OperationResult<IReadOnlyList<SiteExhibitionSummaryServiceModel>>>(
            facade => facade.ListForSiteAsync(Ct)));

    private static async Task<IReadOnlyList<SiteMembershipPlanServiceModel>> PlansAsync(SiteTestHost host) =>
        await Ok(host.WithAsync<IMembershipsFacade, OperationResult<IReadOnlyList<SiteMembershipPlanServiceModel>>>(
            facade => facade.ListForSiteAsync(Ct)));

    private static async Task<CountedCursorPage<SiteShopItemSummaryServiceModel>> ShopAsync(SiteTestHost host, SiteShopListViewModel? request = null) =>
        await Ok(host.WithAsync<IShopFacade, OperationResult<CountedCursorPage<SiteShopItemSummaryServiceModel>>>(
            facade => facade.ListForSiteAsync(request ?? new SiteShopListViewModel(), Ct)));

    private static async Task<IReadOnlyList<SitemapEntryServiceModel>> SitemapAsync(SiteTestHost host) =>
        await Ok(host.WithAsync<ISiteFacade, OperationResult<IReadOnlyList<SitemapEntryServiceModel>>>(facade => facade.ListSitemapAsync(Ct)));

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code);
        return result.Value!;
    }
}
