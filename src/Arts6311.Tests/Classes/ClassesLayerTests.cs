using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Classes.Business;
using Arts6311.Domain.Modules.Classes.Data;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.Validation;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;
using Arts6311.Tests.Classes.Infrastructure;
using Arts6311.Tests.Infrastructure;
using FakeInstructorsFacade = Arts6311.Tests.Instructors.Infrastructure.FakeInstructorsFacade;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Classes;

/// <summary>
/// Each layer against fakes of the one below (backend.md "Testing"): the DataLayer's call order and failure mapping,
/// Business's rules and outcome mapping, and the facade's validation and its hand-offs to Media and Instructors.
/// </summary>
public sealed class ClassesLayerTests
{
    private const string AnyTag = "\"0123456789abcdef0123456789abcdef\"";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Starts = new(2026, 10, 24, 19, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- DataLayer ----

    [Fact]
    public async Task TheDataLayerStagesTheSkusAndTheAuditRowBeforeItsOneSave()
    {
        var (dataLayer, repository, skus, _, log) = DataLayer();
        var offering = new FakeClassesDataLayer().Add(skus: ["CLS-3-1"]);
        repository.Rows.Add(offering);
        var before = offering.RowVersion;

        var outcome = await dataLayer.UpdateAsync(
            offering,
            new SkuChanges(
                [new SkuReservation("CLS-3-2", offering.Runs[0].Id)],
                [new SkuReservation("CLS-3-1", offering.Runs[0].Id)]),
            "editor-2",
            "Updated: runs.",
            pathChange: null,
            Ct);

        Assert.Equal(ClassWriteOutcome.Saved, outcome);

        // Releases first, so a SKU moving between this class's own runs is free by the time it is taken again.
        Assert.Equal(
            ["sku-release:CLS-3-1", "sku-reserve:CLS-3-2", "audit:class-offering.updated", "save"],
            log.Calls);
        Assert.Equal(["CLS-3-1"], skus.Released);
        Assert.Equal(("editor-2", Now), (offering.UpdatedBy, offering.UpdatedAt));
        Assert.NotEqual(before, offering.RowVersion);
    }

    [Fact]
    public async Task APublishedSlugChangeAndEveryStatusChangeStageTheirMessageBeforeTheOneSave()
    {
        var (dataLayer, repository, _, _, log) = DataLayer();
        var store = new FakeClassesDataLayer();
        var offering = store.Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));
        repository.Rows.Add(offering);

        await dataLayer.UpdateAsync(
            offering, SkuChanges.None, "editor-1", "Updated: slug.", new PublishedPathChange("/classes/a", "/classes/b"), Ct);
        log.Calls.Clear();
        await dataLayer.TransitionAsync(offering, PublishCommand.Unpublish, PublishStatus.Published, "editor-1", Ct);

        Assert.Equal(["outbox:site.revalidate-requested", "audit:class-offering.unpublished", "save"], log.Calls);
    }

    [Fact]
    public async Task AFailedSaveIsMappedToTheClashItCanProveAndRethrownOtherwise()
    {
        var (dataLayer, repository, skus, _, _) = DataLayer();
        var offering = new FakeClassesDataLayer().Add();
        var reservation = new SkuChanges([new SkuReservation("CLS-3-1", Guid.NewGuid())], []);

        repository.SaveFailure = new DbUpdateConcurrencyException("moved");
        var conflict = await dataLayer.UpdateAsync(offering, reservation, "editor-1", "Updated.", null, Ct);

        repository.SaveFailure = new DbUpdateException("unique");
        repository.SlugExists = true;
        var slugTaken = await dataLayer.UpdateAsync(offering, reservation, "editor-1", "Updated.", null, Ct);

        repository.SaveFailure = new DbUpdateException("unique");
        repository.SlugExists = false;
        skus.Owners["CLS-3-1"] = new SkuOwner(CatalogOwnerTypes.ShopItem, Guid.NewGuid());
        var skuTaken = await dataLayer.UpdateAsync(offering, reservation, "editor-1", "Updated.", null, Ct);

        repository.SaveFailure = new DbUpdateException("something else");
        skus.Owners.Clear();

        Assert.Equal(ClassWriteOutcome.Conflict, conflict);
        Assert.Equal(ClassWriteOutcome.SlugTaken, slugTaken);
        Assert.Equal(ClassWriteOutcome.SkuTaken, skuTaken);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            dataLayer.UpdateAsync(offering, reservation, "editor-1", "Updated.", null, Ct));
    }

    // ---- Business ----

    [Fact]
    public async Task BusinessNumbersSuggestedSkusFromTheOfferingNumber()
    {
        var (business, dataLayer) = Business();
        dataLayer.NextOfferingNumber = 7;

        var created = await business.CreateAsync(
            new ClassCreateViewModel("Course", Runs: [Run(1), Run(0)]),
            ClassMediaCheck.None,
            ClassInstructorCheck.None,
            "editor-1",
            Ct);

        // Numbered in the order the runs meet, not the order they were sent.
        Assert.Equal(["CLS-7-1", "CLS-7-2"], created.Value!.Model.Runs.Select(run => run.Sku));
        Assert.Equal(Starts, created.Value.Model.Runs[0].Sessions[0].StartsAt);
        Assert.Equal(7, created.Value.Model.OfferingNumber);
        Assert.Equal(["CLS-7-1", "CLS-7-2"], dataLayer.LastSkuChanges!.Reserve.Select(reservation => reservation.Sku));
        Assert.Empty(dataLayer.LastSkuChanges.Release);
    }

    [Fact]
    public async Task BusinessRefusesASkuAnotherRecordHoldsAndOneRepeatedInTheRequest()
    {
        var (business, dataLayer) = Business();
        dataLayer.SkuOwners["TAKEN-1"] = new SkuOwner(CatalogOwnerTypes.ShopItem, Guid.NewGuid());

        var taken = await business.CreateAsync(
            new ClassCreateViewModel("A", Runs: [Run(0) with { Sku = "TAKEN-1" }]),
            ClassMediaCheck.None,
            ClassInstructorCheck.None,
            "editor-1",
            Ct);
        var repeated = await business.CreateAsync(
            new ClassCreateViewModel("B", Runs: [Run(0) with { Sku = "SAME-1" }, Run(1) with { Sku = "SAME-1" }]),
            ClassMediaCheck.None,
            ClassInstructorCheck.None,
            "editor-1",
            Ct);

        Assert.Equal(CatalogErrorCodes.SkuTaken, taken.Error!.Code);
        Assert.Equal(ErrorCodes.ValidationFailed, repeated.Error!.Code);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessRefusesAnIllegalTransitionAndAnEditThatChangesNothingIsNotAWrite()
    {
        var (business, dataLayer) = Business();
        var offering = dataLayer.Add(PublishStatus.Published, publishedAt: Now);
        var tag = Tag(offering.RowVersion);

        var republished = await business.TransitionAsync(offering.Id, PublishCommand.Publish, tag, null, null, "editor-1", Ct);
        var nothing = await business.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Title: offering.Title), tag, null, null, "editor-1", Ct);

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, republished.Error!.Code);
        Assert.True(nothing.Succeeded);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessRefusesAnInstructorWhoIsNotThereAndAnArchivedOneForARunThatDidNotHaveThem()
    {
        var (business, dataLayer) = Business();
        var offering = dataLayer.Add(skus: ["CLS-3-1"]);
        var kept = offering.Runs[0];
        var tag = Tag(offering.RowVersion);
        var retired = Guid.NewGuid();
        var check = new ClassInstructorCheck(new Dictionary<Guid, ClassInstructorFact>
        {
            [FakeClassesDataLayer.InstructorId] = new("Gwen Schilling", PublishStatus.Archived),
            [retired] = new("Retired Person", PublishStatus.Archived),
        });

        // The run that already has Gwen keeps her, archived or not; a new run can't be given an archived instructor,
        // and nobody can be given one the module does not have.
        var keeping = await business.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Runs: [Keep(kept) with { Label = "Fall" }]),
            tag,
            null,
            check,
            "editor-1",
            Ct);
        var adding = await business.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Runs: [Keep(kept), Run(2) with { InstructorId = retired }]),
            tag,
            null,
            check,
            "editor-1",
            Ct);
        var unknown = await business.CreateAsync(
            new ClassCreateViewModel("B", Runs: [Run(0) with { InstructorId = Guid.NewGuid() }]),
            ClassMediaCheck.None,
            check,
            "editor-1",
            Ct);

        Assert.True(keeping.Succeeded, keeping.Error?.Message);
        Assert.Contains("Retired Person is archived", adding.Error!.FieldErrors["runs"][0], StringComparison.Ordinal);
        Assert.Equal(ErrorCodes.ValidationFailed, unknown.Error!.Code);
        Assert.Single(dataLayer.Writes);
    }

    [Fact]
    public async Task ARunSquareHoldsKeepsItsPriceAndCannotBeRemoved()
    {
        var (business, dataLayer) = Business();
        var offering = dataLayer.Add(skus: ["CLS-3-1", "CLS-3-2"]);
        var sold = offering.Runs[0];
        sold.PriceCents = 3000;
        sold.SeatsRemaining = 4;
        var tag = Tag(offering.RowVersion);
        var gwen = Known(FakeClassesDataLayer.InstructorId);

        var repriced = await business.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Runs: [Keep(sold) with { StartingPriceCents = 4000 }, Keep(offering.Runs[1])]),
            tag, null, gwen, "editor-1", Ct);
        var removed = await business.UpdateAsync(
            offering.Id, new ClassUpdateViewModel(Runs: [Keep(offering.Runs[1])]), tag, null, gwen, "editor-1", Ct);
        var shrunk = await business.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Runs: [Keep(sold) with { Capacity = 5 }, Keep(offering.Runs[1])]),
            tag, null, gwen, "editor-1", Ct);
        var unchanged = await business.UpdateAsync(
            offering.Id,
            new ClassUpdateViewModel(Runs: [Keep(sold) with { StartingPriceCents = null, Capacity = 6 }, Keep(offering.Runs[1])]),
            tag, null, gwen, "editor-1", Ct);

        Assert.Contains("change-price", repriced.Error!.FieldErrors["runs"][0], StringComparison.Ordinal);
        Assert.Contains("in Square", removed.Error!.FieldErrors["runs"][0], StringComparison.Ordinal);
        Assert.Contains("6 of 10 seats taken", shrunk.Error!.FieldErrors["runs"][0], StringComparison.Ordinal);

        // Leaving the price out of a run Square holds keeps it; six seats is exactly what is taken.
        Assert.True(unchanged.Succeeded, unchanged.Error?.Message);
        Assert.Equal((3000L, 6), (sold.StartingPriceCents!.Value, sold.Capacity));
        Assert.False(unchanged.Value!.Model.Runs.Single(run => run.Id == sold.Id).StartingPriceEditable);
    }

    [Fact]
    public async Task PublishingNeedsEveryRunToHaveAnInstructorASessionAndAPrice()
    {
        var (business, dataLayer) = Business();
        var offering = dataLayer.Add(skus: ["CLS-3-1", "CLS-3-2"]);
        offering.Runs[0].InstructorId = null;
        offering.Runs[1].Sessions.Clear();
        offering.Runs[1].StartingPriceCents = null;

        // Instructors vouches for the one run that still names someone; the other run names nobody at all.
        var teaching = new ClassInstructorCheck(new Dictionary<Guid, ClassInstructorFact>
        {
            [offering.Runs[1].InstructorId!.Value] = new("Gwen Schilling", PublishStatus.Published),
        });

        var refused = await business.TransitionAsync(
            offering.Id, PublishCommand.Publish, Tag(offering.RowVersion), ClassMediaCheck.None, teaching,
            "editor-1", Ct);

        Assert.Equal(
            ["Run CLS-3-1 needs an instructor.", "Run CLS-3-2 needs at least one session.", "Run CLS-3-2 needs a price. Enter 0 for a free class."],
            refused.Error!.FieldErrors["runs"]);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessTakesAScheduledPublishBackToDraftWhenPublishingIsNoLongerPossible()
    {
        var (business, dataLayer) = Business();
        var offering = dataLayer.Add(PublishStatus.Scheduled, summary: null, startingPriceCents: null);
        offering.ScheduledFor = Now.AddMinutes(-1);

        var outcome = await business.PublishScheduledAsync(offering.Id, Now, ClassMediaCheck.None, ClassInstructorCheck.None, Ct);

        Assert.Equal(ScheduledPublishOutcome.Unscheduled, outcome);
        Assert.Equal((PublishStatus.Scheduled, PublishStatus.Draft), (dataLayer.LastAbandon!.Value.From, offering.Status));
        Assert.Contains("ready to publish", dataLayer.LastAbandon.Value.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BusinessReportsLostWhenAnotherWorkerSavedFirstAndSkippedWhenItIsNoLongerDue()
    {
        var (business, dataLayer) = Business();
        var due = dataLayer.Add(PublishStatus.Scheduled);
        due.ScheduledFor = Now.AddMinutes(-1);
        var draft = dataLayer.Add();

        dataLayer.Outcome = ClassWriteOutcome.Conflict;
        var lost = await business.PublishScheduledAsync(due.Id, Now, ClassMediaCheck.None, ClassInstructorCheck.None, Ct);
        var skipped = await business.PublishScheduledAsync(draft.Id, Now, ClassMediaCheck.None, ClassInstructorCheck.None, Ct);

        Assert.Equal(ScheduledPublishOutcome.Lost, lost);
        Assert.Equal(ScheduledPublishOutcome.Skipped, skipped);
    }

    [Fact]
    public async Task BusinessMapsTheDataLayersClashOutcomesToTheirCodes()
    {
        var (business, dataLayer) = Business();

        dataLayer.Outcome = ClassWriteOutcome.SlugTaken;
        var slug = await business.CreateAsync(
            new ClassCreateViewModel("Course"), ClassMediaCheck.None, ClassInstructorCheck.None, "editor-1", Ct);

        dataLayer.Outcome = ClassWriteOutcome.SkuTaken;
        dataLayer.SkuOwners["CLS-4-1"] = new SkuOwner(CatalogOwnerTypes.ShopItem, Guid.NewGuid());
        var sku = await business.CreateAsync(
            new ClassCreateViewModel("Course", Runs: [Run(0) with { Sku = "CLS-4-2" }]),
            ClassMediaCheck.None,
            ClassInstructorCheck.None,
            "editor-1",
            Ct);

        Assert.Equal(ContentErrorCodes.SlugTaken, slug.Error!.Code);
        Assert.Equal(CatalogErrorCodes.SkuTaken, sku.Error!.Code);
    }

    // ---- Facade ----

    [Fact]
    public async Task TheFacadeRefusesAnInvalidListBeforeBusinessSeesIt()
    {
        var (facade, business, _) = Facade();

        var result = await facade.ListAsync(new ClassListViewModel(Limit: 0), Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task TheFacadeAnswers428BeforeItValidatesTheBody()
    {
        var (facade, business, _) = Facade();

        var result = await facade.UpdateAsync(
            RecordingClassesBusiness.Offering.Id, new ClassUpdateViewModel(), ifMatch: null, "editor-1", Ct);

        Assert.Equal(ErrorCodes.PreconditionRequired, result.Error!.Code);
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task TheFacadeAsksMediaOnceAboutTheHeroAndNotABodyPictureTheSanitizerDrops()
    {
        // ADR 0045: no pictures in a body at this time, so only the hero is asked about.
        var (facade, business, media) = Facade();
        var hero = Guid.NewGuid();
        var inBody = Guid.NewGuid();
        media.MissingAltText.Add(hero);

        var result = await facade.CreateAsync(
            new ClassCreateViewModel("Course", Body: $"""<img src="/media/{inBody}/800.webp" alt="A press">""", HeroMediaId: hero),
            "editor-1",
            Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(1, media.Checks);
        Assert.Equal(hero, business.CreateMedia!.HeroMediaId);
        Assert.False(business.CreateMedia.HeroHasAltText);
        Assert.Empty(business.CreateMedia.AskedBodyImages);
    }

    [Fact]
    public async Task TheFacadeRefusesAHeroMediaDoesNotHaveAndDoesNotAskAboutOneThatIsNotSent()
    {
        var (facade, business, media) = Facade();
        var hero = Guid.NewGuid();
        media.Missing.Add(hero);

        var refused = await facade.CreateAsync(new ClassCreateViewModel("Course", HeroMediaId: hero), "editor-1", Ct);
        var plain = await facade.UpdateAsync(
            RecordingClassesBusiness.Offering.Id, new ClassUpdateViewModel(Title: "Renamed"), AnyTag, "editor-1", Ct);

        Assert.Contains("heroMediaId", refused.Error!.FieldErrors.Keys);
        Assert.True(plain.Succeeded);
        Assert.Null(business.UpdateMedia);
        Assert.Equal(1, media.Checks);
    }

    [Fact]
    public async Task TheFacadeAsksInstructorsOnceAboutTheRunsAndFinishesTheAnswerWithThem()
    {
        var instructors = new FakeInstructorsFacade();
        var gwen = instructors.Add("Gwen Schilling");
        var (facade, business, _) = Facade(instructors);
        var runId = Guid.NewGuid();
        business.Current = RecordingClassesBusiness.Offering with
        {
            Runs = [new(runId, null, null, null, "America/Chicago", "CLS-3-1", 10, 3000, "USD", true, null, null, false, false, [])],
        };
        business.RunInstructors[runId] = gwen.Id;

        var result = await facade.CreateAsync(
            new ClassCreateViewModel("Course", Runs: [new ClassRunViewModel(10, InstructorId: gwen.Id), new ClassRunViewModel(8, InstructorId: gwen.Id)]),
            "editor-1",
            Ct);

        Assert.True(result.Succeeded, result.Error?.Message);
        Assert.Equal(new ClassInstructorFact("Gwen Schilling", PublishStatus.Published), business.CreateInstructors!.Known[gwen.Id]);
        Assert.Equal(("Gwen Schilling", "gwen-schilling"), (result.Value!.Runs[0].Instructor!.Name, result.Value.Runs[0].Instructor!.Slug));
        Assert.Equal(2, instructors.Lookups);
    }

    [Fact]
    public async Task TheFacadeNamesTheListsInstructorsInOneQuestion()
    {
        var instructors = new FakeInstructorsFacade();
        var gwen = instructors.Add("Gwen Schilling");
        var nick = instructors.Add("Nicholas Waguespack");
        var (facade, business, _) = Facade(instructors);
        business.ListRows.Add(new ClassListRow(
            Guid.NewGuid(), "Intro", "intro", PublishStatus.Draft, null, null, [nick.Id, gwen.Id, Guid.NewGuid()], null, 3, null, Now));

        var page = await facade.ListAsync(new ClassListViewModel(), Ct);

        Assert.Equal(["Gwen Schilling", "Nicholas Waguespack"], page.Value!.Items.Single().Instructors);
        Assert.Equal(1, instructors.Lookups);
    }

    [Fact]
    public async Task ThePublishAndScheduleCommandsVouchForTheHeroWithTwoBusinessCalls()
    {
        var (facade, business, _) = Facade();
        var when = Now.AddDays(1);

        await facade.PublishAsync(RecordingClassesBusiness.Offering.Id, AnyTag, "editor-1", Ct);
        await facade.ScheduleAsync(
            RecordingClassesBusiness.Offering.Id, new ContentScheduleViewModel(when), AnyTag, "editor-1", Ct);

        Assert.Equal(["get", "transition:Publish", "get", "schedule"], business.Calls);
        Assert.Equal(when, business.LastScheduledFor);
        Assert.NotNull(business.ScheduleMedia);
    }

    [Fact]
    public async Task AScheduledPublishOfARecordThatIsGoneIsSkippedAndAnythingElseThrows()
    {
        var (facade, business, _) = Facade();

        var gone = await facade.PublishScheduledAsync(Guid.NewGuid(), Now, Ct);

        Assert.Equal(ScheduledPublishOutcome.Skipped, gone);
        Assert.Equal(["get"], business.Calls);
    }

    /// <summary>A new run of ten seats with one session, <paramref name="week"/> weeks after <see cref="Starts"/>.</summary>
    private static ClassRunViewModel Run(int week) =>
        new(10, Sessions: [new ClassSessionViewModel(Starts.AddDays(7 * week), Starts.AddDays(7 * week).AddHours(3))]);

    /// <summary>A stored run sent back as it is, as the editor would.</summary>
    private static ClassRunViewModel Keep(Arts6311.Domain.Modules.Classes.Data.Entities.ClassRun run) => new(
        run.Capacity,
        run.Id,
        run.Label,
        run.InstructorId,
        run.Cadence,
        run.Sku,
        run.DisplayTimeZone,
        run.StartingPriceCents,
        [.. run.Sessions.Select(session => new ClassSessionViewModel(session.StartsAt, session.EndsAt, session.Id))]);

    private static ClassInstructorCheck Known(Guid id) =>
        new(new Dictionary<Guid, ClassInstructorFact> { [id] = new("Gwen Schilling", PublishStatus.Published) });

    private static string Tag(Guid rowVersion) => $"\"{rowVersion:N}\"";

    private static (ClassesDataLayer, FakeClassesRepository, FakeSkuRegistry, RecordingAuditWriter, CallLog) DataLayer()
    {
        var log = new CallLog();
        var repository = new FakeClassesRepository(log);
        var skus = new FakeSkuRegistry(log);
        var audit = new RecordingAuditWriter(log);
        return (
            new ClassesDataLayer(repository, skus, audit, new RecordingOutboxWriter(log), new FixedCorrelation(), new FixedClock(Now)),
            repository,
            skus,
            audit,
            log);
    }

    private static (ClassesBusiness, FakeClassesDataLayer) Business()
    {
        var dataLayer = new FakeClassesDataLayer();
        return (new ClassesBusiness(dataLayer, new AllowListHtmlSanitizer(), new FixedClock(Now)), dataLayer);
    }

    private static (ClassesFacade, RecordingClassesBusiness, FakeMediaFacade) Facade() => Facade(new FakeInstructorsFacade());

    private static (ClassesFacade, RecordingClassesBusiness, FakeMediaFacade) Facade(FakeInstructorsFacade instructors)
    {
        var business = new RecordingClassesBusiness();
        var media = new FakeMediaFacade();
        var facade = new ClassesFacade(
            business,
            media,
            instructors,
            new AllowListHtmlSanitizer(),
            new ClassCreateValidator(),
            new ClassUpdateValidator(),
            new ClassListValidator(),
            new ContentScheduleValidator(),
            TestAppCache.Create());
        return (facade, business, media);
    }
}
