using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Tests.Classes.Infrastructure;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Arts6311.Domain.Modules.Instructors.Facade;
using Microsoft.Extensions.DependencyInjection;
using Arts6311.Domain.Managers.Concurrency;

namespace Arts6311.Tests.Classes;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: the rules, the runs and their instructors,
/// the SKUs it claims in the shared registry, the audit trail, the atomic save, and what Square owns and the CMS may
/// not write.
/// </summary>
public sealed class ClassesModuleTests
{
    private const string Editor = ClassesTestHost.EditorId;

    private const string Admin = ClassesTestHost.AdminId;

    private static readonly DateTimeOffset FirstSession = new(2026, 10, 24, 19, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreatedClassIsADraftWithItsRunsSuggestedSkusReservedAndAnAuditRow()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        var nick = await host.AddInstructorAsync("Nicholas Waguespack");

        var offering = await CreateAsync(host, new ClassCreateViewModel(
            "  Intro to Screen Printing ",
            Summary: "Burn your first screen.",
            ClassType: "Printmaking",
            Level: "Beginner",
            Runs:
            [
                Run(2, 3) with { Label = "Sunday mornings", InstructorId = nick, Cadence = "Sundays", StartingPriceCents = 30000 },
                Run(0, 2) with { Label = " Saturday afternoons ", InstructorId = gwen, Cadence = "Saturdays", StartingPriceCents = 30000 },
            ]));

        Assert.Equal(
            ("Intro to Screen Printing", "intro-to-screen-printing", PublishStatus.Draft),
            (offering.Title, offering.Slug, offering.Status));

        // Runs by when they start, and each suggestion's number follows that order.
        Assert.Equal(["CLS-1-1", "CLS-1-2"], offering.Runs.Select(run => run.Sku));
        Assert.Equal(["Saturday afternoons", "Sunday mornings"], offering.Runs.Select(run => run.Label));
        Assert.Equal(["Gwen Schilling", "Nicholas Waguespack"], offering.Runs.Select(run => run.Instructor!.Name));
        Assert.Equal([2, 3], offering.Runs.Select(run => run.Sessions.Count));
        Assert.Equal([FirstSession, FirstSession.AddDays(7)], offering.Runs[0].Sessions.Select(session => session.StartsAt));
        Assert.All(offering.Runs, run => Assert.Equal(("America/Chicago", "USD", 30000L), (run.DisplayTimeZone, run.Currency, run.StartingPriceCents!.Value)));
        Assert.All(offering.Runs, run => Assert.True(run.StartingPriceEditable));
        Assert.All(offering.Runs, run => Assert.Equal((null, null, false), (run.PriceCents, run.SeatsRemaining, run.SquareHasRecord)));

        var runIds = (await host.StoredAsync(offering.Id))!.Runs.Select(run => run.Id).ToHashSet();
        Assert.Equal(
            [("CLS-1-1", CatalogOwnerTypes.ClassRun), ("CLS-1-2", CatalogOwnerTypes.ClassRun)],
            (await host.SkusAsync()).Select(row => (row.Sku, row.OwnerType)));
        Assert.All(await host.SkusAsync(), row => Assert.Contains(row.OwnerId, runIds));
        Assert.Equal(["class-offering.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AMadeSlugTakesTheNextFreeNumberButAGivenTakenOneIsRefusedOnTheSlugField()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ClassCreateViewModel("Intro to Scratchboard"));

        // The CMS never sends a slug, so one made from the name can't be refused: there is nothing staff could change.
        var second = await CreateAsync(host, new ClassCreateViewModel("INTRO TO SCRATCHBOARD"));
        var third = await CreateAsync(host, new ClassCreateViewModel("Intro to scratchboard"));
        var given = await host.WithFacadeAsync(facade => facade.CreateAsync(new ClassCreateViewModel("Another Class", Slug: "intro-to-scratchboard"), Editor, Ct));

        Assert.Equal(("intro-to-scratchboard-2", "intro-to-scratchboard-3"), (second.Slug, third.Slug));
        Assert.Equal(ContentErrorCodes.SlugTaken, given.Error!.Code);
        Assert.Contains("slug", given.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task ASkuAnotherClassRunHoldsIsRefused()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ClassCreateViewModel("First Class", Runs: [Run(0)]));

        var second = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("Second Class", Runs: [Run(0) with { Sku = "cls-1-1" }]), Editor, Ct));

        Assert.Equal(CatalogErrorCodes.SkuTaken, second.Error!.Code);
        Assert.Contains("runs", second.Error.FieldErrors.Keys);
        Assert.Contains("CLS-1-1", second.Error.Message);

        // Nothing was written, and the SKU still belongs to the first class's run.
        Assert.Single(await host.SkusAsync());
    }

    [Fact]
    public async Task ASkuAnotherKindOfCatalogRecordHoldsIsRefusedToo()
    {
        // ADR 0037: the registry is shared, so 2.10's artwork and a class run cannot both hold "ART-7".
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
            new ClassCreateViewModel("A Class", Runs: [Run(0) with { Sku = "ART-7" }]), Editor, Ct));

        Assert.Equal(CatalogErrorCodes.SkuTaken, refused.Error!.Code);
    }

    [Fact]
    public async Task ASuggestedSkuSkipsOneAlreadyTakenAndStaffCanEditIt()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var first = await CreateAsync(host, new ClassCreateViewModel("First", Runs: [Run(0)]));

        // The second offering is number 2, so its own suggestions do not collide; an explicit SKU is kept as typed.
        var second = await CreateAsync(host, new ClassCreateViewModel(
            "Second", Runs: [Run(0), Run(1) with { Sku = "workshop.2b" }]));

        Assert.Equal("CLS-1-1", first.Runs.Single().Sku);
        Assert.Equal(["CLS-2-1", "WORKSHOP.2B"], second.Runs.Select(run => run.Sku));
    }

    [Fact]
    public async Task ARunsInstructorMustBeOneTheCentreHasAndNotArchived()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var retired = await host.AddInstructorAsync("Retired Person", PublishStatus.Archived);

        var unknown = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("A", Runs: [Run(0) with { InstructorId = Guid.NewGuid() }]), Editor, Ct));
        var archived = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ClassCreateViewModel("B", Runs: [Run(0) with { InstructorId = retired }]), Editor, Ct));

        Assert.Contains("runs", unknown.Error!.FieldErrors.Keys);
        Assert.Contains("Retired Person is archived", archived.Error!.FieldErrors["runs"][0], StringComparison.Ordinal);
        Assert.Empty(await host.QueryAsync(context => context.Set<ClassOffering>().AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task AnInstructorWhoTeachesARunCannotBeDeletedFromUnderIt()
    {
        // ADR 0044: a Restrict key into Instructors, as a shop item's into Artists (ADR 0033).
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        await CreateAsync(host, new ClassCreateViewModel("Course", Runs: [Run(0) with { InstructorId = gwen }]));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => host.ExecuteAsync(async context =>
        {
            context.Remove(await context.Set<Instructor>().SingleAsync(row => row.Id == gwen, Ct));
            await context.SaveChangesAsync(Ct);
        }));
    }

    [Fact]
    public async Task PublishingNeedsASummaryARunAndEveryRunReady()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var bare = await CreateAsync(host, new ClassCreateViewModel("Bare Class"));
        var unfinished = await CreateAsync(host, new ClassCreateViewModel(
            "Unfinished Class", Summary: "A class.", Runs: [new ClassRunViewModel(10)]));

        var noRuns = await host.WithFacadeAsync(facade => facade.PublishAsync(bare.Id, bare.ETag, Editor, Ct));
        var notReady = await host.WithFacadeAsync(facade => facade.PublishAsync(unfinished.Id, unfinished.ETag, Editor, Ct));

        Assert.Equal(["runs", "summary"], noRuns.Error!.FieldErrors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Run CLS-2-1 needs an instructor.", "Run CLS-2-1 needs at least one session.", "Run CLS-2-1 needs a price. Enter 0 for a free class."],
            notReady.Error!.FieldErrors["runs"]);
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(bare.Id))!.Status);
    }

    [Fact]
    public async Task PublishingStagesOneRevalidationForItsOwnPage()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await ReadyAsync(host);

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, offering.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.True(published.SlugLocked);
        Assert.Equal("Gwen Schilling", published.Runs.Single().Instructor!.Name);

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
        var offering = await ReadyAsync(host);
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
        var offering = await ReadyAsync(host);

        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Title: "Renamed"), "\"0123456789abcdef0123456789abcdef\"", Editor, Ct));
        var missing = await host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, null, Editor, Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        Assert.Equal("Intro to Screen Printing", (await host.StoredAsync(offering.Id))!.Title);
    }

    [Fact]
    public async Task RunsAreReplacedKeepingWhatStaysAndReleasingTheSkusOfWhatGoes()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await CreateAsync(host, new ClassCreateViewModel("Course", Runs: [Run(0, 2), Run(5)]));
        var kept = offering.Runs[0];
        var keptSession = kept.Sessions[0];

        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Runs:
            [
                Keep(kept) with
                {
                    Capacity = 12,
                    Sku = "CLS-1-9",
                    Sessions = [new ClassSessionViewModel(keptSession.StartsAt.AddHours(1), keptSession.EndsAt.AddHours(1), keptSession.Id)],
                },
                Run(9),
            ]),
            offering.ETag,
            Editor,
            Ct)));

        var stayed = edited.Runs.Single(run => run.Id == kept.Id);
        Assert.Equal(["CLS-1-9", "CLS-1-3"], edited.Runs.Select(run => run.Sku));
        Assert.Equal(12, stayed.Capacity);

        // The kept session kept its id and moved an hour; the dropped one is gone.
        Assert.Equal((keptSession.Id, keptSession.StartsAt.AddHours(1)), (stayed.Sessions.Single().Id, stayed.Sessions.Single().StartsAt));

        // CLS-1-1 was renamed and CLS-1-2's run is gone, so neither is reserved any more.
        Assert.Equal(["CLS-1-3", "CLS-1-9"], (await host.SkusAsync()).Select(row => row.Sku));
    }

    [Fact]
    public async Task TheRegistryHoldsExactlyTheRunsSkusAfterEveryWritePath()
    {
        // ADR 0037: the SKU lives both on the run and in the shared registry, and one write path keeps the pair in
        // step. This is what makes that claim true rather than aspirational.
        await using var host = await ClassesTestHost.CreateAsync(Ct);

        var created = await CreateAsync(host, new ClassCreateViewModel("Course", Runs: [Run(0), Run(1)]));
        await AssertRegistryMatches(host, created.Id);

        var kept = created.Runs[0];
        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            created.Id,
            new ClassUpdateViewModel(Runs: [Keep(kept) with { Sku = "COURSE.A" }, Run(2), Run(3)]),
            created.ETag,
            Editor,
            Ct)));
        await AssertRegistryMatches(host, created.Id);

        var dropped = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            created.Id,
            new ClassUpdateViewModel(Runs: [Keep(kept) with { Sku = "COURSE.A" }]),
            renamed.ETag,
            Editor,
            Ct)));
        await AssertRegistryMatches(host, created.Id);

        var cleared = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            created.Id, new ClassUpdateViewModel(Runs: []), dropped.ETag, Editor, Ct)));

        Assert.Empty(cleared.Runs);
        Assert.Empty(await host.SkusAsync());
        Assert.Empty(await host.QueryAsync(context => context.Set<ClassSession>().AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task ARefusedCapacityChangeLeavesEveryRunAsItWas()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await CreateAsync(host, new ClassCreateViewModel("Course", Runs: [Run(0), Run(1)]));
        await SquareReports(host, offering.Runs[1].Id, priceCents: 30000, seatsRemaining: 2);
        var current = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(offering.Id, Ct)));

        // The second run's capacity is below its seats taken, so the whole edit is refused — including the first run's
        // new capacity, which is in the same request.
        var refused = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Runs: [Keep(current.Runs[0]) with { Capacity = 20 }, Keep(current.Runs[1]) with { Capacity = 1 }]),
            current.ETag,
            Editor,
            Ct));

        Assert.Contains("runs", refused.Error!.FieldErrors.Keys);
        var stored = (await host.StoredAsync(offering.Id))!;
        Assert.Equal([10, 10], stored.Runs.Select(run => run.Capacity));
        await AssertRegistryMatches(host, offering.Id);
    }

    [Fact]
    public async Task WhatSquareOwnsIsNeverWrittenFromTheCms()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await CreateAsync(host, new ClassCreateViewModel(
            "Course", Runs: [Run(0) with { StartingPriceCents = 30000 }]));
        var runId = offering.Runs.Single().Id;

        // As the 5.x inbound pull would leave it: Square has the price and has sold four of the ten seats.
        await SquareReports(host, runId, priceCents: 32500, seatsRemaining: 6);
        var current = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(offering.Id, Ct)));
        var run = current.Runs.Single();

        var priced = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Runs: [Keep(run) with { StartingPriceCents = 40000 }]), current.ETag, Editor, Ct));
        var shrunk = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Runs: [Keep(run) with { Capacity = 3 }]), current.ETag, Editor, Ct));
        var dropped = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Runs: []), current.ETag, Editor, Ct));

        Assert.False(run.StartingPriceEditable);
        Assert.Equal((32500L, 6, true), (run.PriceCents!.Value, run.SeatsRemaining!.Value, run.SquareHasRecord));
        Assert.Contains("runs", priced.Error!.FieldErrors.Keys);
        Assert.Contains("runs", shrunk.Error!.FieldErrors.Keys);
        Assert.Contains("runs", dropped.Error!.FieldErrors.Keys);

        var stored = (await host.StoredAsync(offering.Id))!.Runs.Single();
        Assert.Equal((30000L, 32500L, 6, 10), (stored.StartingPriceCents!.Value, stored.PriceCents!.Value, stored.SeatsRemaining!.Value, stored.Capacity));
    }

    [Fact]
    public async Task ACapacityAboveTheSeatsTakenIsAllowed()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var offering = await CreateAsync(host, new ClassCreateViewModel("Course", Runs: [Run(0)]));
        await SquareReports(host, offering.Runs.Single().Id, priceCents: 30000, seatsRemaining: 6);
        var current = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(offering.Id, Ct)));

        var grown = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Runs: [Keep(current.Runs.Single()) with { Capacity = 20 }]), current.ETag, Editor, Ct)));

        Assert.Equal((20, 6), (grown.Runs.Single().Capacity, grown.Runs.Single().SeatsRemaining!.Value));
    }

    [Fact]
    public async Task TheListNamesEachClasssInstructorsOnce()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        var nick = await host.AddInstructorAsync("Nicholas Waguespack");
        await CreateAsync(host, new ClassCreateViewModel(
            "Course", Runs: [Run(0) with { InstructorId = nick }, Run(1) with { InstructorId = gwen }, Run(2) with { InstructorId = nick }]));

        var page = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ClassListViewModel(), Ct)));

        var row = page.Items.Single();
        Assert.Equal(["Gwen Schilling", "Nicholas Waguespack"], row.Instructors);
        Assert.Equal(3, row.RunCount);
        Assert.Equal(FirstSession, row.NextSessionStartsAt);
    }

    [Fact]
    public async Task TheDescriptionIsStoredSanitizedWithoutItsPicturesOrLinks()
    {
        // ADR 0045: a class's picture is its hero, so the body keeps its words and drops the rest.
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var image = await host.AddImageAsync("A press in the studio.");

        var offering = await CreateAsync(host, new ClassCreateViewModel(
            "Course",
            Body: $"""<p onclick="steal()">Bring an <a href="/shop">apron</a>.<script>alert(1)</script></p><img src="/media/{image}/800.webp" alt="A press">"""));

        Assert.Equal("<p>Bring an apron.</p>", offering.Body);
        Assert.Empty(offering.ImageMediaIds);
        Assert.Empty((await host.StoredAsync(offering.Id))!.Images);
    }

    [Fact]
    public async Task AHeroNeedsAltTextBeforeThePublishButNotBeforeTheDraft()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var hero = await host.AddImageAsync(altText: null);
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        var offering = await CreateAsync(host, new ClassCreateViewModel(
            "Course", Summary: "A class.", HeroMediaId: hero, Runs: [Run(0) with { InstructorId = gwen, StartingPriceCents = 3000 }]));

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
            new ClassCreateViewModel("Course", Runs: [Run(0)]), Editor, Ct)));

        Assert.Empty(await host.QueryAsync(context => context.Set<ClassOffering>().AsNoTracking().ToListAsync(Ct)));
        Assert.Empty(await host.QueryAsync(context => context.Set<ClassRun>().AsNoTracking().ToListAsync(Ct)));
        Assert.Empty(await host.SkusAsync());
    }

    [Fact]
    public async Task AFailedRenameLeavesNeitherTheNewSlugNorItsRedirectMessage()
    {
        // A half-commit here is the worst of the three: the slug moves, the redirect message does not, and every link
        // to the old address breaks silently. Disarmed while the class is set up, then armed for the rename itself.
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ClassesTestHost.CreateAsync(Ct, failAudit);
        var offering = await ReadyAsync(host);
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
        var offering = await ReadyAsync(host);

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
        var offering = await ReadyAsync(host);
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
        var ready = await ReadyAsync(host);
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
        var offering = await ReadyAsync(host);
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(offering.Id, offering.ETag, Editor, Ct)));

        var published = await host.WithFacadeAsync(facade => facade.PublishAsync(offering.Id, archived.ETag, Editor, Ct));

        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, published.Error!.Code);
    }

    [Fact]
    public async Task TheDevelopmentSeedAddsTheSpikesClassesOnceWithTheirRunsInstructorsAndSkus()
    {
        await using var host = await ClassesTestHost.CreateAsync(Ct, withSeeders: true);

        await host.SeedAsync(Ct);
        await host.SeedAsync(Ct);

        var all = await host.QueryAsync(context => context.Set<ClassOffering>().AsNoTracking()
            .Include(offering => offering.Runs).ThenInclude(run => run.Sessions)
            .OrderBy(offering => offering.OfferingNumber).ToListAsync(Ct));
        var instructors = await host.QueryAsync(context => context.Set<Instructor>().AsNoTracking()
            .ToDictionaryAsync(instructor => instructor.Id, instructor => instructor.Name, Ct));

        Assert.Equal(ClassDevelopmentSeeder.Classes.Count, all.Count);
        Assert.All(all, offering => Assert.Equal(PublishStatus.Published, offering.Status));
        Assert.All(all, offering => Assert.Equal(ClassDevelopmentSeeder.SeedActor, offering.UpdatedBy));
        Assert.All(all, offering => Assert.Null(offering.HeroMediaId));
        Assert.All(all.SelectMany(offering => offering.Runs), run => Assert.Null(run.PriceCents));
        Assert.All(all.SelectMany(offering => offering.Runs), run => Assert.True(run.InstructorId is { } id && instructors.ContainsKey(id)));

        // The spike's two beginner screen-printing courses are one class with two runs, each with its own teacher.
        var course = all.Single(offering => offering.Title == "Intro to Screen Printing");
        var runs = course.Runs.OrderBy(run => run.Sessions.Min(session => session.StartsAt)).ToList();
        Assert.Equal(["Gwen Schilling", "Nicholas Waguespack"], runs.Select(run => instructors[run.InstructorId!.Value]));
        Assert.Equal([5, 5], runs.Select(run => run.Sessions.Count));

        // The same 2 p.m. in Chicago each week, across the end of daylight saving.
        Assert.Equal(
            [new(2026, 10, 24, 19, 0, 0, TimeSpan.Zero), new(2026, 10, 31, 19, 0, 0, TimeSpan.Zero), new(2026, 11, 7, 20, 0, 0, TimeSpan.Zero)],
            runs[0].Sessions.OrderBy(session => session.StartsAt).Take(3).Select(session => session.StartsAt));

        Assert.Equal(all.SelectMany(offering => offering.Runs).Count(), (await host.SkusAsync()).Count);
        Assert.Single(await host.AuditActionsAsync(), action => action == "class-offering.seeded");
    }

    [Fact]
    public async Task ASeededClassWhoseInstructorIsMissingIsADraftNotPublished()
    {
        // Seeded without the instructors' seeder, so no instructor can be found for any run.
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        using (var scope = host.CreateScope())
        {
            var seeder = new ClassDevelopmentSeeder(
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Arts6311.Domain.Modules.Classes.Business.IClassesBusiness>(scope.ServiceProvider),
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Arts6311.Domain.Modules.Instructors.Facade.IInstructorsFacade>(scope.ServiceProvider),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ClassDevelopmentSeeder>.Instance);
            await seeder.SeedAsync(Ct);
        }

        var all = await host.QueryAsync(context => context.Set<ClassOffering>().AsNoTracking().ToListAsync(Ct));
        Assert.Equal(ClassDevelopmentSeeder.Classes.Count, all.Count);
        Assert.All(all, offering => Assert.Equal((PublishStatus.Draft, (DateTimeOffset?)null), (offering.Status, offering.PublishedAt)));
    }

    /// <summary>
    /// A new run of ten seats with <paramref name="sessions"/> weekly sittings, the first <paramref name="week"/> weeks
    /// after <see cref="FirstSession"/>, and no SKU, instructor or price of its own.
    /// </summary>
    private static ClassRunViewModel Run(int week, int sessions = 1) => new(
        10,
        Sessions: [.. Enumerable.Range(week, sessions).Select(at =>
            new ClassSessionViewModel(FirstSession.AddDays(7 * at), FirstSession.AddDays(7 * at).AddHours(3)))]);

    /// <summary>A run as the CMS returned it, sent back unchanged, as the editor does.</summary>
    private static ClassRunViewModel Keep(ClassRunServiceModel run) => new(
        run.Capacity,
        run.Id,
        run.Label,
        run.Instructor?.Id,
        run.Cadence,
        run.Sku,
        run.DisplayTimeZone,
        run.StartingPriceCents,
        [.. run.Sessions.Select(session => new ClassSessionViewModel(session.StartsAt, session.EndsAt, session.Id))]);

    [Fact]
    public async Task ADeletedRecordIsGoneFromEveryReadAndARestoreBringsItBackAsADraft()
    {
        // ADR 0052: one query filter, so "deleted" means gone from every read — and a restore is a decision to
        // look at the record again, not to publish it.
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var created = await CreateAsync(host, new ClassCreateViewModel("Intro to Scratchboard"));

        var deleted = await host.WithFacadeAsync(facade => facade.DeleteAsync(created.Id, created.ETag, ClassesTestHost.AdminId, Ct));

        Assert.True(deleted.Succeeded, deleted.Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, (await host.WithFacadeAsync(facade => facade.GetAsync(created.Id, Ct))).Error!.Code);
        Assert.Empty((await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ClassListViewModel(), Ct)))).Items);
        Assert.Null(await host.StoredAsync(created.Id));

        // The address is free at once, which is the point: the mistake stops standing in the way.
        var replacement = await CreateAsync(host, new ClassCreateViewModel("Intro to Scratchboard"));
        Assert.Equal("intro-to-scratchboard", replacement.Slug);

        var trash = await Ok(host.WithFacadeAsync(facade => facade.ListDeletedAsync(new ClassListViewModel(), Ct)));
        var row = Assert.Single(trash.Items);
        Assert.Equal("Intro to Scratchboard", row.Title);

        // The replacement holds the address now, so the restore is refused until it does not.
        var refused = await host.WithFacadeAsync(facade => facade.RestoreAsync(created.Id, row.ETag, ClassesTestHost.AdminId, Ct));
        Assert.Equal(ContentErrorCodes.SlugTaken, refused.Error!.Code);

        await host.WithFacadeAsync(facade => facade.DeleteAsync(replacement.Id, replacement.ETag, ClassesTestHost.AdminId, Ct));
        var restored = await Ok(host.WithFacadeAsync(facade => facade.RestoreAsync(created.Id, row.ETag, ClassesTestHost.AdminId, Ct)));

        Assert.Equal(PublishStatus.Draft, restored.Status);
        Assert.Null(restored.ScheduledFor);
        Assert.Contains("class-offering.deleted", await host.AuditActionsAsync());
        Assert.Contains("class-offering.restored", await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AnInstructorAClassRunNamesCannotBeDeletedAndAClassCannotPublishWithoutThem()
    {
        // The two halves of the same rule (ADR 0052, 0044): while a live class names the instructor, the delete is
        // refused; and if the class was itself deleted when the instructor went, publishing it again is refused
        // rather than showing a run with no teacher.
        await using var host = await ClassesTestHost.CreateAsync(Ct);
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        var offering = await CreateAsync(host, new ClassCreateViewModel(
            "Intro to Screen Printing",
            Summary: "Burn your first screen.",
            Runs: [Run(0) with { InstructorId = gwen, StartingPriceCents = 30000 }]));

        var refused = await DeleteInstructorAsync(host, gwen);

        Assert.Equal(ContentErrorCodes.InUse, refused.Error!.Code);
        Assert.Contains("“Intro to Screen Printing”", refused.Error.Message, StringComparison.Ordinal);

        // With the class itself deleted, nothing live names her, so she goes.
        await host.WithFacadeAsync(facade => facade.DeleteAsync(offering.Id, offering.ETag, Admin, Ct));
        Assert.True((await DeleteInstructorAsync(host, gwen)).Succeeded);

        // Restoring the class brings back a run naming an instructor who is gone. It comes back as a Draft, and
        // publishing it says so rather than going live without a teacher.
        var tag = EntityTag.Format((await host.QueryAsync(context => context.Set<ClassOffering>()
            .IgnoreQueryFilters().AsNoTracking().Where(row => row.Id == offering.Id)
            .Select(row => row.RowVersion).SingleAsync(Ct))));
        var restored = await Ok(host.WithFacadeAsync(facade => facade.RestoreAsync(offering.Id, tag, Admin, Ct)));

        var cannotPublish = await host.WithFacadeAsync(facade => facade.PublishAsync(restored.Id, restored.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, cannotPublish.Error!.Code);
        Assert.Contains("instructor has been deleted", cannotPublish.Error.FieldErrors["runs"][0], StringComparison.Ordinal);
    }

    private static async Task<OperationResult> DeleteInstructorAsync(ClassesTestHost host, Guid instructorId)
    {
        var version = await host.QueryAsync(context => context.Set<Instructor>().AsNoTracking()
            .Where(row => row.Id == instructorId).Select(row => row.RowVersion).SingleAsync(Ct));

        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInstructorsFacade>()
            .DeleteAsync(instructorId, EntityTag.Format(version), Admin, Ct);
    }

    /// <summary>A draft with everything publishing requires, taught by Gwen.</summary>
    private static async Task<ClassServiceModel> ReadyAsync(ClassesTestHost host)
    {
        var gwen = await host.AddInstructorAsync("Gwen Schilling");
        return await CreateAsync(host, new ClassCreateViewModel(
            "Intro to Screen Printing",
            Summary: "Burn your first screen.",
            Runs: [Run(0) with { InstructorId = gwen, StartingPriceCents = 30000 }]));
    }

    private static async Task<ClassServiceModel> CreateAsync(ClassesTestHost host, ClassCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    /// <summary>What the 5.x inbound pull writes: the projections, and only those (square.md).</summary>
    private static Task SquareReports(ClassesTestHost host, Guid runId, long priceCents, int seatsRemaining) =>
        host.ExecuteAsync(async context =>
        {
            var run = await context.Set<ClassRun>().SingleAsync(row => row.Id == runId, Ct);
            run.PriceCents = priceCents;
            run.SeatsRemaining = seatsRemaining;
            run.SoldOut = seatsRemaining == 0;
            await context.SaveChangesAsync(Ct);
        });

    /// <summary>Every run's SKU has exactly one registry row, owned by that run, and there are no others.</summary>
    private static async Task AssertRegistryMatches(ClassesTestHost host, Guid offeringId)
    {
        var runs = (await host.StoredAsync(offeringId))!.Runs;
        var rows = await host.SkusAsync();

        Assert.Equal(
            runs.Select(run => (run.Sku, CatalogOwnerTypes.ClassRun, run.Id)).OrderBy(row => row.Sku),
            rows.Select(row => (row.Sku, row.OwnerType, row.OwnerId)).OrderBy(row => row.Sku));
    }

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }
}
