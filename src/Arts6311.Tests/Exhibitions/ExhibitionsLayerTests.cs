using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Exhibitions.Business;
using Arts6311.Domain.Modules.Exhibitions.Data;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.Validation;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;
using Arts6311.Tests.Exhibitions.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Exhibitions;

/// <summary>
/// Each layer against fakes of the one below (backend.md "Testing"): the DataLayer's call order and failure mapping,
/// Business's rules and outcome mapping, and the facade's validation and its hand-offs to Media and Artists.
/// </summary>
public sealed class ExhibitionsLayerTests
{
    private const string AnyTag = "\"0123456789abcdef0123456789abcdef\"";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 10, 24);
    private static readonly DateOnly End = new(2026, 12, 5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- DataLayer ----

    [Fact]
    public async Task TheDataLayerStampsAndStagesTheAuditRowBeforeItsOneSave()
    {
        var (dataLayer, repository, audit, log) = DataLayer();
        var exhibition = new FakeExhibitionsDataLayer().Add();
        repository.Rows.Add(exhibition);
        var before = exhibition.RowVersion;

        var outcome = await dataLayer.UpdateAsync(exhibition, "editor-2", "Updated: artists.", pathChange: null, Ct);

        Assert.Equal(ExhibitionWriteOutcome.Saved, outcome);
        Assert.Equal(["audit:exhibition.updated", "save"], log.Calls);
        Assert.Equal(("editor-2", Now), (exhibition.UpdatedBy, exhibition.UpdatedAt));
        Assert.NotEqual(before, exhibition.RowVersion);
        Assert.Equal("editor-2", audit.Entries.Single().ActorUserId);
    }

    [Fact]
    public async Task APublishedSlugChangeIsStagedBeforeTheOneSave()
    {
        var (dataLayer, repository, _, log) = DataLayer();
        var exhibition = new FakeExhibitionsDataLayer().Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));
        repository.Rows.Add(exhibition);

        await dataLayer.UpdateAsync(
            exhibition, "editor-1", "Updated: slug.", new PublishedPathChange("/exhibitions/a", "/exhibitions/b"), Ct);

        Assert.Equal(["outbox:redirects.slug-changed", "audit:exhibition.updated", "save"], log.Calls);
    }

    [Fact]
    public async Task ACreateAddsBeforeAuditingAndSavingAndATransitionAuditsBothStatuses()
    {
        var (dataLayer, _, audit, log) = DataLayer();
        var exhibition = new FakeExhibitionsDataLayer().Add(PublishStatus.Archived);

        await dataLayer.CreateAsync(new FakeExhibitionsDataLayer().Add(), "editor-1", Ct);
        await dataLayer.TransitionAsync(exhibition, PublishCommand.Archive, PublishStatus.Published, "admin-1", Ct);

        // The transition's revalidation is staged before its audit row and both before its one save (ADR 0036).
        Assert.Equal(
            ["add", "audit:exhibition.created", "save", "outbox:site.revalidate-requested", "audit:exhibition.archived", "save"],
            log.Calls);
        var archived = audit.Entries[^1];
        Assert.Equal(("status:Published", "status:Archived"), (archived.BeforeReference, archived.AfterReference));
    }

    [Fact]
    public async Task AConcurrencyFailureIsAConflictAndAUniqueFailureIsATakenSlugOnlyWhenItIs()
    {
        var (dataLayer, repository, _, log) = DataLayer();

        repository.SaveFailure = new DbUpdateConcurrencyException("stale");
        Assert.Equal(ExhibitionWriteOutcome.Conflict, await dataLayer.UpdateAsync(new FakeExhibitionsDataLayer().Add(), "e", "Updated.", pathChange: null, Ct));
        Assert.Equal("discard", log.Calls[^1]);

        repository.SaveFailure = new DbUpdateException("unique");
        repository.SlugExists = true;
        Assert.Equal(ExhibitionWriteOutcome.SlugTaken, await dataLayer.CreateAsync(new FakeExhibitionsDataLayer().Add(), "e", Ct));

        // Any other database failure is a fault, not a refusal the editor could fix.
        repository.SaveFailure = new DbUpdateException("something else");
        repository.SlugExists = false;
        await Assert.ThrowsAsync<DbUpdateException>(() => dataLayer.CreateAsync(new FakeExhibitionsDataLayer().Add(), "e", Ct));
    }

    // ---- Business ----

    [Fact]
    public async Task BusinessBuildsADraftWithItsCreditsInOrderAndTheReceptionInUtc()
    {
        var (business, dataLayer) = Business();
        var (zed, amy) = (Guid.NewGuid(), Guid.NewGuid());
        var sixPm = new DateTimeOffset(2026, 10, 24, 18, 0, 0, TimeSpan.FromHours(-5));

        var result = await business.CreateAsync(
            new ExhibitionCreateViewModel(
                " Boats & Pianos ", Start, End, Summary: "Line one.\r\nLine two.",
                Reception: new(sixPm, sixPm.AddHours(3), "  "), ArtistIds: [zed, amy]),
            "editor-1",
            Ct);

        var detail = result.Value!;
        Assert.Equal(("Boats & Pianos", "boats-pianos", "Line one.\nLine two."), (detail.Title, detail.Slug, detail.Summary));
        Assert.Equal([zed, amy], detail.ArtistIds);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero), detail.Reception!.StartsAt);
        Assert.Equal(TimeSpan.Zero, detail.Reception.StartsAt.Offset);
        Assert.Null(detail.Reception.Note);
        Assert.Equal(PublishStatus.Draft, detail.Status);
        Assert.Equal(["create"], dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessComparesOneSentDateWithTheOneStored()
    {
        var (business, dataLayer) = Business();
        var exhibition = dataLayer.Add();
        var etag = EntityTag.Format(exhibition.RowVersion);

        var endTooEarly = await business.UpdateAsync(exhibition.Id, new ExhibitionUpdateViewModel(EndDate: Start.AddDays(-1)), etag, null, "e", Ct);
        var startTooLate = await business.UpdateAsync(exhibition.Id, new ExhibitionUpdateViewModel(StartDate: End.AddDays(1)), etag, null, "e", Ct);
        var moved = await business.UpdateAsync(exhibition.Id, new ExhibitionUpdateViewModel(EndDate: End.AddDays(7)), etag, null, "e", Ct);

        Assert.Contains("endDate", endTooEarly.Error!.FieldErrors.Keys);
        Assert.Contains("startDate", startTooLate.Error!.FieldErrors.Keys);
        Assert.Equal(End.AddDays(7), moved.Value!.EndDate);
        Assert.Equal(["update:Updated: dates."], dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessReordersKeepsAndReplacesCreditsInPlace()
    {
        var (business, dataLayer) = Business();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var exhibition = dataLayer.Add(artistIds: [a, b]);
        var credits = exhibition.Artists.ToDictionary(credit => credit.ArtistId);

        var result = await business.UpdateAsync(
            exhibition.Id, new ExhibitionUpdateViewModel(ArtistIds: [c, b]), EntityTag.Format(exhibition.RowVersion), null, "e", Ct);

        Assert.Equal([c, b], result.Value!.ArtistIds);

        // The credit that stays is the same tracked row, moved; a removed and re-added one would collide on the key.
        Assert.Same(credits[b], exhibition.Artists.Single(credit => credit.ArtistId == b));
        Assert.Equal(1, credits[b].Position);
        Assert.DoesNotContain(exhibition.Artists, credit => credit.ArtistId == a);
    }

    [Fact]
    public async Task SendingTheCreditsAndDatesItAlreadyHasWritesNothing()
    {
        var (business, dataLayer) = Business();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var exhibition = dataLayer.Add(artistIds: [a, b]);

        var result = await business.UpdateAsync(
            exhibition.Id,
            new ExhibitionUpdateViewModel(StartDate: Start, EndDate: End, ArtistIds: [a, b]),
            EntityTag.Format(exhibition.RowVersion),
            null,
            "e",
            Ct);

        Assert.True(result.Succeeded);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessChecksTheVersionBeforeAnyRule()
    {
        var (business, dataLayer) = Business();
        var exhibition = dataLayer.Add(PublishStatus.Archived);

        var result = await business.TransitionAsync(
            exhibition.Id, PublishCommand.Publish, EntityTag.Format(Guid.NewGuid()), ExhibitionHeroCheck.None, "e", Ct);

        Assert.Equal(ErrorCodes.ConcurrencyConflict, result.Error!.Code);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task PublishReportsAMissingSummaryAndAnUndescribedHeroTogether()
    {
        var (business, dataLayer) = Business();
        var hero = Guid.NewGuid();
        var exhibition = dataLayer.Add(hero: hero, summary: null);

        var result = await business.TransitionAsync(
            exhibition.Id, PublishCommand.Publish, EntityTag.Format(exhibition.RowVersion),
            new ExhibitionHeroCheck(hero, Exists: true, HasAltText: false), "e", Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Equal(["heroMediaId", "summary"], result.Error.FieldErrors.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(dataLayer.Writes);
        Assert.Equal(PublishStatus.Draft, exhibition.Status);
    }

    [Fact]
    public async Task BusinessRefusesAPublishWhoseHeroCheckIsMissingOrForAnotherImage()
    {
        var (business, dataLayer) = Business();
        var hero = Guid.NewGuid();
        var exhibition = dataLayer.Add(hero: hero);
        var etag = EntityTag.Format(exhibition.RowVersion);

        var noCheck = await business.TransitionAsync(exhibition.Id, PublishCommand.Publish, etag, null, "e", Ct);
        var otherImage = await business.TransitionAsync(
            exhibition.Id, PublishCommand.Publish, etag, new ExhibitionHeroCheck(Guid.NewGuid(), true, true), "e", Ct);

        Assert.Equal(ErrorCodes.ConcurrencyConflict, noCheck.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, otherImage.Error!.Code);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessSetsTheFirstPublishTimeOnceAndHandsOnTheStatusItLeft()
    {
        var (business, dataLayer) = Business();
        var exhibition = dataLayer.Add(PublishStatus.Scheduled);
        exhibition.ScheduledFor = Now.AddHours(1);

        var result = await business.TransitionAsync(
            exhibition.Id, PublishCommand.Publish, EntityTag.Format(exhibition.RowVersion), ExhibitionHeroCheck.None, "e", Ct);

        Assert.Equal(Now, result.Value!.PublishedAt);
        Assert.Null(result.Value.ScheduledFor);
        Assert.Equal((PublishCommand.Publish, PublishStatus.Scheduled), dataLayer.LastTransition);
    }

    [Fact]
    public async Task APublishedExhibitionKeepsItsSummaryAndTakesOnlyADescribedHero()
    {
        var (business, dataLayer) = Business();
        var published = dataLayer.Add(PublishStatus.Published, publishedAt: Now);
        var draft = dataLayer.Add();
        var image = Guid.NewGuid();
        var bare = new ExhibitionHeroCheck(image, Exists: true, HasAltText: false);

        var cleared = await business.UpdateAsync(
            published.Id, new ExhibitionUpdateViewModel(Summary: ""), EntityTag.Format(published.RowVersion), null, "e", Ct);
        var bareHero = await business.UpdateAsync(
            published.Id, new ExhibitionUpdateViewModel(HeroMediaId: image), EntityTag.Format(published.RowVersion), bare, "e", Ct);
        var onDraft = await business.UpdateAsync(
            draft.Id, new ExhibitionUpdateViewModel(Summary: "", HeroMediaId: image), EntityTag.Format(draft.RowVersion), bare, "e", Ct);

        Assert.Contains("summary", cleared.Error!.FieldErrors.Keys);
        Assert.Contains("heroMediaId", bareHero.Error!.FieldErrors.Keys);
        Assert.True(onDraft.Succeeded);
        Assert.Null(onDraft.Value!.Summary);
    }

    [Fact]
    public async Task ASlugRaceOrAConflictAtTheSaveMapsToItsCode()
    {
        var (business, dataLayer) = Business();

        dataLayer.Outcome = ExhibitionWriteOutcome.SlugTaken;
        var create = await business.CreateAsync(new ExhibitionCreateViewModel("Queue", Start, End), "e", Ct);

        dataLayer.Outcome = ExhibitionWriteOutcome.Conflict;
        var published = dataLayer.Add(PublishStatus.Published, publishedAt: Now);
        var unpublish = await business.TransitionAsync(
            published.Id, PublishCommand.Unpublish, EntityTag.Format(published.RowVersion), null, "e", Ct);

        Assert.Equal(ContentErrorCodes.SlugTaken, create.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, unpublish.Error!.Code);
    }

    [Fact]
    public async Task TheSeedAddsOnlyTheSlugsNotInUseAsPublishedWithTheirCredits()
    {
        var (business, dataLayer) = Business();
        dataLayer.Add();
        var artist = Guid.NewGuid();

        var added = await business.SeedAsync(
            [
                new ExhibitionSeed("Boats Pianos", Start, End, "Taken slug.", null, []),
                new ExhibitionSeed("Queue", Start, End, "A solo show.", null, [artist]),
            ],
            "system:test",
            Ct);

        Assert.Equal(1, added);
        var queue = Assert.Single(dataLayer.Seeded);
        Assert.Equal(("queue", PublishStatus.Published, Now), (queue.Slug, queue.Status, queue.PublishedAt!.Value));
        Assert.Equal([artist], queue.Artists.Select(credit => credit.ArtistId));
    }

    // ---- Facade ----

    [Fact]
    public async Task TheFacadeStopsAnInvalidRequestBeforeBusinessMediaOrArtists()
    {
        var (facade, business, media, artists) = Facade();

        var result = await facade.CreateAsync(
            new ExhibitionCreateViewModel(" ", Start, End, HeroMediaId: Guid.NewGuid(), ArtistIds: [Guid.NewGuid()]), "e", Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Empty(business.Calls);
        Assert.Equal((0, 0), (media.Checks, artists.Lookups));
    }

    [Fact]
    public async Task TheFacadeRefusesAnArtistNotOnTheRosterOrArchived()
    {
        var (facade, business, _, artists) = Facade();
        var jane = artists.Add("Jane Doe");
        var gone = artists.Add("Gone Artist", PublishStatus.Archived);

        var unknown = await facade.CreateAsync(
            new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [jane.Id, Guid.NewGuid()]), "e", Ct);
        var archived = await facade.UpdateAsync(
            Guid.NewGuid(), new ExhibitionUpdateViewModel(ArtistIds: [jane.Id, gone.Id]), AnyTag, "e", Ct);

        Assert.Contains("artistIds", unknown.Error!.FieldErrors.Keys);
        Assert.Contains("Gone Artist", Assert.Single(archived.Error!.FieldErrors["artistIds"]), StringComparison.Ordinal);
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task TheFacadeLetsADraftArtistBeCreditedAndNamesTheCreditsInTheExhibitionsOrder()
    {
        var (facade, business, _, artists) = Facade();
        var zed = artists.Add("Zed Artist", PublishStatus.Draft);
        var amy = artists.Add("Amy Artist");
        business.Current = RecordingExhibitionsBusiness.Exhibition with { ArtistIds = [zed.Id, amy.Id] };

        var result = await facade.CreateAsync(
            new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [zed.Id, amy.Id]), "e", Ct);

        Assert.Equal(["Zed Artist", "Amy Artist"], result.Value!.Artists.Select(artist => artist.Name));
        Assert.Equal(PublishStatus.Draft, result.Value.Artists[0].Status);
        Assert.Equal("America/Chicago", result.Value.DisplayTimeZone);
    }

    [Fact]
    public async Task AReadWithoutCreditsAsksArtistsNothing()
    {
        var (facade, _, _, artists) = Facade();

        var result = await facade.GetAsync(RecordingExhibitionsBusiness.Exhibition.Id, Ct);

        Assert.Empty(result.Value!.Artists);
        Assert.Equal(0, artists.Lookups);
    }

    [Fact]
    public async Task TheFacadeRefusesAHeroMediaDoesNotKnowButLetsADraftTakeOneWithoutAltText()
    {
        var (facade, business, media, _) = Facade();
        var unknown = Guid.NewGuid();
        var bare = Guid.NewGuid();
        media.Missing.Add(unknown);
        media.MissingAltText.Add(bare);

        var missing = await facade.CreateAsync(new ExhibitionCreateViewModel("Show", Start, End, HeroMediaId: unknown), "e", Ct);
        var draft = await facade.CreateAsync(new ExhibitionCreateViewModel("Show", Start, End, HeroMediaId: bare), "e", Ct);
        await facade.UpdateAsync(Guid.NewGuid(), new ExhibitionUpdateViewModel(HeroMediaId: bare), AnyTag, "e", Ct);

        Assert.Contains("heroMediaId", missing.Error!.FieldErrors.Keys);
        Assert.True(draft.Succeeded);
        Assert.Equal(new ExhibitionHeroCheck(bare, Exists: true, HasAltText: false), business.UpdateHero);
    }

    [Fact]
    public async Task PublishAsksMediaAboutTheCurrentHeroAndUnpublishAndArchiveNeverDo()
    {
        var (facade, business, media, _) = Facade();
        var hero = Guid.NewGuid();
        business.Current = RecordingExhibitionsBusiness.Exhibition with { HeroMediaId = hero };
        media.MissingAltText.Add(hero);

        await facade.PublishAsync(business.Current.Id, business.Current.ETag, "e", Ct);
        Assert.Equal(new ExhibitionHeroCheck(hero, Exists: true, HasAltText: false), business.LastTransition!.Value.Hero);

        await facade.UnpublishAsync(business.Current.Id, business.Current.ETag, "e", Ct);
        await facade.ArchiveAsync(business.Current.Id, business.Current.ETag, "admin-1", Ct);

        Assert.Equal(1, media.Checks);
        Assert.Equal(["get", "transition:Publish", "transition:Unpublish", "transition:Archive"], business.Calls);
    }

    [Fact]
    public async Task AWriteWithoutIfMatchIsA428BeforeAnythingElseIsAsked()
    {
        var (facade, business, media, artists) = Facade();

        var update = await facade.UpdateAsync(
            Guid.NewGuid(), new ExhibitionUpdateViewModel(HeroMediaId: Guid.NewGuid(), ArtistIds: [Guid.NewGuid()]), null, "e", Ct);
        var publish = await facade.PublishAsync(business.Current.Id, " ", "e", Ct);

        // An unknown id too: the missing header is the answer, not the missing row.
        var unpublish = await facade.UnpublishAsync(Guid.NewGuid(), null, "e", Ct);
        var archive = await facade.ArchiveAsync(Guid.NewGuid(), null, "admin-1", Ct);

        Assert.Equal(ErrorCodes.PreconditionRequired, update.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, publish.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, unpublish.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, archive.Error!.Code);
        Assert.Empty(business.Calls);
        Assert.Equal((0, 0), (media.Checks, artists.Lookups));
    }

    private static (ExhibitionsDataLayer, FakeExhibitionsRepository, RecordingAuditWriter, CallLog) DataLayer()
    {
        var log = new CallLog();
        var repository = new FakeExhibitionsRepository(log);
        var audit = new RecordingAuditWriter(log);
        return (new ExhibitionsDataLayer(repository, audit, new RecordingOutboxWriter(log), new FixedCorrelation(), new FixedClock(Now)), repository, audit, log);
    }

    private static (ExhibitionsBusiness, FakeExhibitionsDataLayer) Business()
    {
        var dataLayer = new FakeExhibitionsDataLayer();
        return (new ExhibitionsBusiness(dataLayer, new FixedClock(Now)), dataLayer);
    }

    private static (ExhibitionsFacade, RecordingExhibitionsBusiness, FakeMediaFacade, FakeArtistsFacade) Facade()
    {
        var business = new RecordingExhibitionsBusiness();
        var media = new FakeMediaFacade();
        var artists = new FakeArtistsFacade();
        var facade = new ExhibitionsFacade(
            business, media, artists, new ExhibitionCreateValidator(), new ExhibitionUpdateValidator(),
            new ExhibitionListValidator(), new ContentScheduleValidator());
        return (facade, business, media, artists);
    }
}
