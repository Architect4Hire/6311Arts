using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Tests.Classes.Infrastructure;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Classes;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: the rules, the SKUs it claims in the
/// shared registry, the audit trail, the atomic save, and what Square owns and the CMS may not write.
/// </summary>
public sealed class ClassesModuleTests
{
    private const string Editor = ClassesTestHost.EditorId;

    private static readonly DateTimeOffset FirstSession = new(2026, 10, 24, 19, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreatedClassIsADraftWithSuggestedSkusReservedAndAnAuditRow()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);

        var offering = await CreateAsync(host, new ClassCreateViewModel(
            "  Intro to Screen Printing ",
            Summary: "Burn your first screen.",
            ClassType: "Printmaking",
            Level: "Beginner",
            Instructor: "Gwen Schilling",
            Cadence: "Saturdays",
            StartingPriceCents: 30000,
            Sessions: [Session(1), Session(0)]));

        Assert.Equal(
            ("Intro to Screen Printing", "intro-to-screen-printing", PublishStatus.Draft),
            (offering.Title, offering.Slug, offering.Status));
        Assert.Equal(30000, offering.StartingPriceCents);
        Assert.Equal("USD", offering.Currency);
        Assert.True(offering.StartingPriceEditable);

        // Ordered by start, and the suggestion's number follows that order.
        Assert.Equal(["CLS-1-1", "CLS-1-2"], offering.Sessions.Select(session => session.Sku));
        Assert.Equal([FirstSession, FirstSession.AddDays(7)], offering.Sessions.Select(session => session.StartsAt));
        Assert.All(offering.Sessions, session => Assert.Equal("America/Chicago", session.DisplayTimeZone));
        Assert.All(offering.Sessions, session => Assert.Null(session.PriceCents));
        Assert.All(offering.Sessions, session => Assert.Null(session.SeatsRemaining));
        Assert.All(offering.Sessions, session => Assert.False(session.SquareHasRecord));

        var sessionIds = (await host.StoredAsync(offering.Id))!.Sessions.Select(session => session.Id).ToHashSet();
        Assert.Equal(
            [("CLS-1-1", CatalogOwnerTypes.ClassSession), ("CLS-1-2", CatalogOwnerTypes.ClassSession)],
            (await host.SkusAsync()).Select(row => (row.Sku, row.OwnerType)));
        Assert.All(await host.SkusAsync(), row => Assert.Contains(row.OwnerId, sessionIds));
        Assert.Equal(["class-offering.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ATakenSlugIsRefusedOnTheSlugField()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ClassCreateViewModel("Intro to Scratchboard"));

        var second = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("INTRO TO SCRATCHBOARD"), Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, second.Error!.Code);
        Assert.Contains("slug", second.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task ASkuAnotherClassSessionHoldsIsRefused()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ClassCreateViewModel("First Class", Sessions: [Session(0)]));

        var second = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Second Class", Sessions: [Session(0) with { Sku = "cls-1-1" }]), Editor, Ct));

        Assert.Equal(CatalogErrorCodes.SkuTaken, second.Error!.Code);
        Assert.Contains("sessions", second.Error.FieldErrors.Keys);
        Assert.Contains("CLS-1-1", second.Error.Message);

        // Nothing was written, and the SKU still belongs to the first class's session.
        Assert.Single(await host.SkusAsync());
    }

    [Fact]
    public async Task ASkuAnotherKindOfCatalogRecordHoldsIsRefusedToo()
    {
        // ADR 0037: the registry is shared, so 2.10's artwork and a class session cannot both hold "ART-7".
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        await host.ExecuteAsync(async context =>
        {
            context.Add(new CatalogSku
            {
                Sku = "ART-7",
                OwnerType = CatalogOwnerTypes.ShopItem,
                OwnerId = Guid.NewGuid(),
                CreatedAt = host.Time.GetUtcNow(),
            });
            await context.SaveChangesAsync(Ct);
        });

        var refused = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("A Class", Sessions: [Session(0) with { Sku = "ART-7" }]), Editor, Ct));

        Assert.Equal(CatalogErrorCodes.SkuTaken, refused.Error!.Code);
    }

    [Fact]
    public async Task ASuggestedSkuSkipsOneAlreadyTakenAndStaffCanEditIt()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var first = await CreateAsync(host, new ClassCreateViewModel("First", Sessions: [Session(0)]));

        // The second offering is number 2, so its own suggestions do not collide; an explicit SKU is kept as typed.
        var second = await CreateAsync(host, new ClassCreateViewModel(
            "Second", Sessions: [Session(0), Session(1) with { Sku = "workshop.2b" }]));

        Assert.Equal("CLS-1-1", first.Sessions.Single().Sku);
        Assert.Equal(["CLS-2-1", "WORKSHOP.2B"], second.Sessions.Select(session => session.Sku));
    }

    [Fact]
    public async Task PublishingNeedsASummaryAStartingPriceAndASession()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await CreateAsync(host, new ClassCreateViewModel("Bare Class"));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, offering.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Equal(
            ["sessions", "startingPriceCents", "summary"],
            refused.Error.FieldErrors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(offering.Id))!.Status);
    }

    [Fact]
    public async Task PublishingStagesOneRevalidationForItsOwnPage()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ready(host);

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, offering.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.True(published.SlugLocked);

        var message = Assert.Single(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync(Ct)));
        Assert.Equal(SiteRevalidateRequested.MessageType, message.Type);
        var staged = SiteRevalidateRequested.TryParse(message.PayloadJson)!;
        Assert.Equal(("class-offering", offering.Id), (staged.EntityType, staged.EntityId));
        Assert.Equal([$"/classes/{published.Slug}"], staged.Paths);
        Assert.Equal(["class-offering.created", "class-offering.published"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ARenameAfterPublishStagesItsRedirect()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ready(host);
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, offering.ETag, Editor, Ct)));

        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Slug: "screen-printing"), published.ETag, Editor, Ct)));

        Assert.Equal("screen-printing", renamed.Slug);
        var payload = Assert.Single(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == RedirectSlugChanged.MessageType)
            .Select(message => message.PayloadJson).ToListAsync(Ct)));
        var change = RedirectSlugChanged.TryParse(payload)!;
        Assert.Equal(
            ("class-offering", $"/classes/{published.Slug}", "/classes/screen-printing"),
            (change.ContentType, change.FromPath, change.ToPath));
    }

    [Fact]
    public async Task AStaleOrMissingIfMatchIsRefusedBeforeAnythingIsWritten()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ready(host);

        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Title: "Renamed"), "\"0123456789abcdef0123456789abcdef\"", Editor, Ct));
        var missing = await host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, null, Editor, Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        Assert.Equal("Intro to Screen Printing", (await host.StoredAsync(offering.Id))!.Title);
    }

    [Fact]
    public async Task SessionsAreReplacedKeepingWhatStaysAndReleasingTheSkusOfWhatGoes()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Course", Sessions: [Session(0), Session(1)]), Editor, Ct)));
        var kept = offering.Sessions[0];

        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Sessions:
            [
                new ClassSessionViewModel(kept.StartsAt, kept.EndsAt, 12, kept.Id, Sku: "CLS-1-9"),
                Session(2),
            ]),
            offering.ETag,
            Editor,
            Ct)));

        Assert.Equal([kept.Id], edited.Sessions.Where(session => session.Id == kept.Id).Select(session => session.Id));
        Assert.Equal(["CLS-1-9", "CLS-1-3"], edited.Sessions.Select(session => session.Sku));
        Assert.Equal(12, edited.Sessions.Single(session => session.Id == kept.Id).Capacity);

        // CLS-1-1 was renamed and CLS-1-2's session is gone, so neither is reserved any more.
        Assert.Equal(["CLS-1-3", "CLS-1-9"], (await host.SkusAsync()).Select(row => row.Sku));
    }

    [Fact]
    public async Task TheRegistryHoldsExactlyTheSessionsSkusAfterEveryWritePath()
    {
        // ADR 0037: the SKU lives both on the session and in the shared registry, and one write path keeps the pair
        // in step. This is what makes that claim true rather than aspirational.
        await using var host = await ClassesTestHost.CreateAsync(Ct);

        var created = await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Course", Sessions: [Session(0), Session(1)]), Editor, Ct)));
        await AssertRegistryMatches(host, created.Id);

        var kept = created.Sessions[0];
        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            created.Id,
            new ClassUpdateViewModel(Sessions:
            [
                new ClassSessionViewModel(kept.StartsAt, kept.EndsAt, kept.Capacity, kept.Id, Sku: "COURSE.A"),
                Session(2),
                Session(3),
            ]),
            created.ETag,
            Editor,
            Ct)));
        await AssertRegistryMatches(host, created.Id);

        var dropped = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            created.Id,
            new ClassUpdateViewModel(Sessions:
            [
                new ClassSessionViewModel(kept.StartsAt, kept.EndsAt, kept.Capacity, kept.Id, Sku: "COURSE.A"),
            ]),
            renamed.ETag,
            Editor,
            Ct)));
        await AssertRegistryMatches(host, created.Id);

        var cleared = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            created.Id, new ClassUpdateViewModel(Sessions: []), dropped.ETag, Editor, Ct)));

        Assert.Empty(cleared.Sessions);
        Assert.Empty(await host.SkusAsync());
    }

    [Fact]
    public async Task ARefusedCapacityChangeLeavesEverySessionAsItWas()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Course", Sessions: [Session(0), Session(1)]), Editor, Ct)));
        await SquareReports(host, offering.Sessions[1].Id, priceCents: 30000, seatsRemaining: 2);
        var current = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(offering.Id, Ct)));

        // The second session's capacity is below its seats taken, so the whole edit is refused — including the
        // first session's new time, which is in the same request.
        var refused = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Sessions:
            [
                Session(0) with { Id = offering.Sessions[0].Id, Capacity = 20 },
                Session(1) with { Id = offering.Sessions[1].Id, Capacity = 1 },
            ]),
            current.ETag,
            Editor,
            Ct));

        Assert.Contains("sessions", refused.Error!.FieldErrors.Keys);
        var stored = (await host.StoredAsync(offering.Id))!;
        Assert.Equal([10, 10], stored.Sessions.OrderBy(session => session.StartsAt).Select(session => session.Capacity));
        await AssertRegistryMatches(host, offering.Id);
    }

    [Fact]
    public async Task WhatSquareOwnsIsNeverWrittenFromTheCms()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Course", StartingPriceCents: 30000, Sessions: [Session(0)]), Editor, Ct)));
        var sessionId = offering.Sessions.Single().Id;

        // As the 5.x inbound pull would leave it: Square has the price and has sold four of the ten seats.
        await SquareReports(host, sessionId, priceCents: 32500, seatsRemaining: 6);
        var current = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(offering.Id, Ct)));

        var priced = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(StartingPriceCents: 40000), current.ETag, Editor, Ct));
        var shrunk = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Sessions: [Session(0) with { Id = sessionId, Capacity = 3 }]),
            current.ETag,
            Editor,
            Ct));
        var dropped = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Sessions: []), current.ETag, Editor, Ct));

        Assert.False(current.StartingPriceEditable);
        Assert.Equal((32500L, 6, true), (current.Sessions.Single().PriceCents, current.Sessions.Single().SeatsRemaining, current.Sessions.Single().SquareHasRecord));
        Assert.Contains("startingPriceCents", priced.Error!.FieldErrors.Keys);
        Assert.Contains("sessions", shrunk.Error!.FieldErrors.Keys);
        Assert.Contains("sessions", dropped.Error!.FieldErrors.Keys);

        var stored = (await host.StoredAsync(offering.Id))!;
        Assert.Equal((30000L, 32500L, 6), (stored.StartingPriceCents, stored.Sessions.Single().PriceCents, stored.Sessions.Single().SeatsRemaining));
        Assert.Equal(10, stored.Sessions.Single().Capacity);
    }

    [Fact]
    public async Task ACapacityAboveTheSeatsTakenIsAllowed()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Course", Sessions: [Session(0)]), Editor, Ct)));
        var sessionId = offering.Sessions.Single().Id;
        await SquareReports(host, sessionId, priceCents: 30000, seatsRemaining: 6);
        var current = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(offering.Id, Ct)));

        var grown = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Sessions: [Session(0) with { Id = sessionId, Capacity = 20 }]),
            current.ETag,
            Editor,
            Ct)));

        Assert.Equal(20, grown.Sessions.Single().Capacity);
        Assert.Equal(6, grown.Sessions.Single().SeatsRemaining);
    }

    [Fact]
    public async Task TheDescriptionIsStoredSanitizedWithItsPicturesReferenced()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var image = await host.AddImageAsync("A press in the studio.");

        var offering = await CreateAsync(host, new ClassCreateViewModel(
            "Course",
            Body: $"""<p onclick="steal()">Bring an apron.<script>alert(1)</script></p><img src="/media/{image}/800.webp" alt="A press">"""));

        Assert.DoesNotContain("script", offering.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", offering.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([image], offering.ImageMediaIds);
        Assert.Equal([image], (await host.StoredAsync(offering.Id))!.Images.Select(reference => reference.MediaAssetId));
    }

    [Fact]
    public async Task APictureWithoutAltTextOrOutsideTheLibraryIsRefused()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var image = await host.AddImageAsync("A press in the studio.");

        var undescribed = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("A", Body: $"""<img src="/media/{image}/800.webp">"""), Editor, Ct));
        var absent = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("B", Body: $"""<img src="/media/{Guid.NewGuid()}/800.webp" alt="Gone">"""), Editor, Ct));

        Assert.Contains("body", undescribed.Error!.FieldErrors.Keys);
        Assert.Contains("body", absent.Error!.FieldErrors.Keys);
    }

    [Fact]
    public async Task AHeroNeedsAltTextBeforeThePublishButNotBeforeTheDraft()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var hero = await host.AddImageAsync(altText: null);
        var offering = await CreateAsync(host, new ClassCreateViewModel(
            "Course", Summary: "A class.", StartingPriceCents: 3000, HeroMediaId: hero, Sessions: [Session(0)]));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, offering.ETag, Editor, Ct));

        Assert.Equal(hero, offering.HeroMediaId);
        Assert.Contains("heroMediaId", refused.Error!.FieldErrors.Keys);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheClassNorItsSkuReservations()
    {
        var failAudit = new FailInsertInto("AuditLogs");
        await using var host = await ClassesTestHost.CreateAsync(Ct, failAudit);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Course", Sessions: [Session(0)]), Editor, Ct)));

        Assert.Empty(await host.QueryAsync(context => context.Set<ClassOffering>().AsNoTracking().ToListAsync(Ct)));
        Assert.Empty(await host.SkusAsync());
    }

    [Fact]
    public async Task AFailedRenameLeavesNeitherTheNewSlugNorItsRedirectMessage()
    {
        // Classes had the create-path atomicity test but neither of the two staging paths the other five modules
        // both cover (outbox review). A half-commit here is the worst of the three: the slug moves, the redirect
        // message does not, and every link to the old address breaks silently.
        // Disarmed while the class is set up, then armed for the rename itself.
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ClassesTestHost.CreateAsync(Ct, failAudit);
        var offering = await Ready(host);
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, offering.ETag, Editor, Ct)));

        failAudit.Armed = true;

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Slug: "screen-printing"), published.ETag, Editor, Ct)));

        Assert.True(failAudit.Fired, "The interceptor never fired, so the save was not actually broken.");

        Assert.Equal(published.Slug, (await host.StoredAsync(offering.Id))!.Slug);
        Assert.Empty(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == RedirectSlugChanged.MessageType).ToListAsync(Ct)));
    }

    [Fact]
    public async Task AFailedStatusChangeLeavesNeitherTheStatusNorItsRevalidation()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ClassesTestHost.CreateAsync(Ct, failAudit);
        var offering = await Ready(host);

        failAudit.Armed = true;

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(
            facade => facade.PublishAsync(offering.Id, offering.ETag, Editor, Ct)));

        Assert.True(failAudit.Fired, "The interceptor never fired, so the save was not actually broken.");

        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(offering.Id))!.Status);
        Assert.Empty(await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task AScheduledClassIsPublishedByTheWorkerOnceAndThenSkipped()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ready(host);
        var when = host.Time.GetUtcNow().AddHours(2);

        var scheduled = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            offering.Id, new ContentScheduleViewModel(when), offering.ETag, Editor, Ct)));
        var due = await host.WithFacadeAsync(facade => facade.ListDueAsync(when, 10, Ct));
        var first = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(offering.Id, when, Ct));
        var second = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(offering.Id, when, Ct));

        Assert.Equal((PublishStatus.Scheduled, when), (scheduled.Status, scheduled.ScheduledFor));
        Assert.Equal([offering.Id], due);
        Assert.Equal(ScheduledPublishOutcome.Published, first);
        Assert.Equal(ScheduledPublishOutcome.Skipped, second);

        var stored = (await host.StoredAsync(offering.Id))!;
        Assert.Equal((PublishStatus.Published, (DateTimeOffset?)null), (stored.Status, stored.ScheduledFor));
        Assert.Equal(ScheduledPublishPolicy.Actor, stored.UpdatedBy);
    }

    [Fact]
    public async Task AScheduleIsRefusedForATimeThatHasPassedOrAClassThatCannotPublish()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var ready = await Ready(host);
        var bare = await CreateAsync(host, new ClassCreateViewModel("Bare"));

        var past = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            ready.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddMinutes(-1)), ready.ETag, Editor, Ct));
        var unready = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            bare.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddDays(1)), bare.ETag, Editor, Ct));

        Assert.Contains("scheduledFor", past.Error!.FieldErrors.Keys);
        Assert.Equal(ErrorCodes.ValidationFailed, unready.Error!.Code);
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(ready.Id))!.Status);
    }

    [Fact]
    public async Task AnArchivedClassRefusesEveryStatusCommand()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await Ready(host);
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(offering.Id, offering.ETag, Editor, Ct)));

        var published = await host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, archived.ETag, Editor, Ct));

        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, published.Error!.Code);
    }

    [Fact]
    public async Task TheDevelopmentSeedAddsTheSpikesClassesOnceWithTheirSkus()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct, withSeeders: true);

        await host.SeedAsync(Ct);
        await host.SeedAsync(Ct);

        var all = await host.QueryAsync(context => context.Set<ClassOffering>().AsNoTracking()
            .Include(offering => offering.Sessions).OrderBy(offering => offering.OfferingNumber).ToListAsync(Ct));

        Assert.Equal(ClassDevelopmentSeeder.Classes.Count, all.Count);
        Assert.All(all, offering => Assert.Equal(PublishStatus.Published, offering.Status));
        Assert.All(all, offering => Assert.Equal(ClassDevelopmentSeeder.SeedActor, offering.UpdatedBy));
        Assert.All(all, offering => Assert.Null(offering.HeroMediaId));
        Assert.All(all.SelectMany(offering => offering.Sessions), session => Assert.Null(session.PriceCents));

        var course = all.Single(offering => offering.Title == "Intro to Screen Printing");
        Assert.Equal(5, course.Sessions.Count);

        // The same 2 p.m. in Chicago each week, across the end of daylight saving.
        Assert.Equal(
            [new(2026, 10, 24, 19, 0, 0, TimeSpan.Zero), new(2026, 10, 31, 19, 0, 0, TimeSpan.Zero), new(2026, 11, 7, 20, 0, 0, TimeSpan.Zero)],
            course.Sessions.OrderBy(session => session.StartsAt).Take(3).Select(session => session.StartsAt));

        Assert.Equal(
            all.SelectMany(offering => offering.Sessions).Count(),
            (await host.SkusAsync()).Count);
        Assert.Single(await host.AuditActionsAsync(), action => action == "class-offering.seeded");
    }

    /// <summary>A sitting <paramref name="week"/> weeks after the first, with ten seats and no SKU of its own.</summary>
    private static ClassSessionViewModel Session(int week) =>
        new(FirstSession.AddDays(7 * week), FirstSession.AddDays(7 * week).AddHours(3), 10);

    /// <summary>A draft with everything publishing requires.</summary>
    private static Task<ClassServiceModel> Ready(ClassesTestHost host) =>
        CreateAsync(host, new ClassCreateViewModel(
            "Intro to Screen Printing", Summary: "Burn your first screen.", StartingPriceCents: 30000,
            Sessions: [Session(0)]));

    private static async Task<ClassServiceModel> CreateAsync(ClassesTestHost host, ClassCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    /// <summary>What the 5.x inbound pull writes: the projections, and only those (square.md).</summary>
    private static Task SquareReports(ClassesTestHost host, Guid sessionId, long priceCents, int seatsRemaining) =>
        host.ExecuteAsync(async context =>
        {
            var session = await context.Set<ClassSession>().SingleAsync(row => row.Id == sessionId, Ct);
            session.PriceCents = priceCents;
            session.SeatsRemaining = seatsRemaining;
            session.SoldOut = seatsRemaining == 0;
            await context.SaveChangesAsync(Ct);
        });

    /// <summary>Every session's SKU has exactly one registry row, owned by that session, and there are no others.</summary>
    private static async Task AssertRegistryMatches(ClassesTestHost host, Guid offeringId)
    {
        var sessions = (await host.StoredAsync(offeringId))!.Sessions;
        var rows = await host.SkusAsync();

        Assert.Equal(
            sessions.Select(session => (session.Sku, CatalogOwnerTypes.ClassSession, session.Id)).OrderBy(row => row.Sku),
            rows.Select(row => (row.Sku, row.OwnerType, row.OwnerId)).OrderBy(row => row.Sku));
    }

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }
}
