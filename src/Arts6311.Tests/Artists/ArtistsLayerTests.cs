using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Business;
using Arts6311.Domain.Modules.Artists.Data;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Arts6311.Tests.Infrastructure;

namespace Arts6311.Tests.Artists;

/// <summary>
/// Each layer against fakes of the one below (backend.md "Testing"): the DataLayer's call order and failure mapping,
/// Business's rules and outcome mapping, and the facade's validation and Media hand-off.
/// </summary>
public sealed class ArtistsLayerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- DataLayer ----

    [Fact]
    public async Task TheDataLayerStampsAndStagesTheAuditRowBeforeItsOneSave()
    {
        var (dataLayer, repository, audit, log) = DataLayer();
        var artist = new FakeArtistsDataLayer().Add();
        repository.Rows.Add(artist);
        var before = artist.RowVersion;

        var outcome = await dataLayer.UpdateAsync(artist, "editor-2", "Updated: bio.", pathChange: null, Ct);

        Assert.Equal(ArtistWriteOutcome.Saved, outcome);
        Assert.Equal(["outbox:site.revalidate-requested", "audit:artist.updated", "save"], log.Calls);
        Assert.Equal("editor-2", artist.UpdatedBy);
        Assert.Equal(Now, artist.UpdatedAt);
        Assert.NotEqual(before, artist.RowVersion);
        Assert.Equal("editor-2", audit.Entries.Single().ActorUserId);
    }

    [Fact]
    public async Task APublishedSlugChangeIsStagedBeforeTheOneSaveWithTheRenamesTime()
    {
        var (dataLayer, repository, _, log) = DataLayer();
        var artist = new FakeArtistsDataLayer().Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));
        repository.Rows.Add(artist);

        await dataLayer.UpdateAsync(artist, "editor-1", "Updated: slug.", new PublishedPathChange("/artists/jane-doe", "/artists/jane-q-doe"), Ct);

        Assert.Equal(
            ["outbox:redirects.slug-changed", "outbox:site.revalidate-requested", "audit:artist.updated", "save"],
            log.Calls);
    }

    [Fact]
    public async Task ADraftArtistsEditIsRevalidatedTooBecauseCreditsShowTheirName()
    {
        // ADR 0042: an unpublished artist is still credited by name on published work, so their rename changes what
        // the site shows, unlike a draft of any other type.
        var (dataLayer, repository, _, log) = DataLayer();
        var artist = new FakeArtistsDataLayer().Add(PublishStatus.Draft);
        repository.Rows.Add(artist);

        await dataLayer.UpdateAsync(artist, "editor-1", "Updated: name.", pathChange: null, Ct);

        Assert.Equal(["outbox:site.revalidate-requested", "audit:artist.updated", "save"], log.Calls);
    }

    [Fact]
    public async Task TheDataLayerAuditsATransitionWithBothStatuses()
    {
        var (dataLayer, repository, audit, log) = DataLayer();
        var artist = new FakeArtistsDataLayer().Add(PublishStatus.Archived);
        repository.Rows.Add(artist);

        await dataLayer.TransitionAsync(artist, PublishCommand.Archive, PublishStatus.Published, "admin-1", Ct);

        var entry = audit.Entries.Single();
        Assert.Equal("artist.archived", entry.Action);
        Assert.Equal("status:Published", entry.BeforeReference);
        Assert.Equal("status:Archived", entry.AfterReference);
        // The revalidation is staged before the audit row and both before the one save (outbox.md, ADR 0036).
        Assert.Equal(["outbox:site.revalidate-requested", "audit:artist.archived", "save"], log.Calls);
    }

    [Fact]
    public async Task ACreateAddsBeforeAuditingAndSaving()
    {
        var (dataLayer, _, _, log) = DataLayer();

        await dataLayer.CreateAsync(new FakeArtistsDataLayer().Add(), "editor-1", Ct);

        Assert.Equal(["add", "audit:artist.created", "save"], log.Calls);
    }

    [Fact]
    public async Task AConcurrencyFailureIsAConflictAndTheChangesAreDiscarded()
    {
        var (dataLayer, repository, _, log) = DataLayer();
        repository.SaveFailure = new DbUpdateConcurrencyException("stale");

        var outcome = await dataLayer.UpdateAsync(new FakeArtistsDataLayer().Add(), "editor-1", "Updated.", pathChange: null, Ct);

        Assert.Equal(ArtistWriteOutcome.Conflict, outcome);
        Assert.Equal("discard", log.Calls[^1]);
    }

    [Fact]
    public async Task AUniqueFailureIsATakenSlugOnlyWhenTheSlugReallyIsTaken()
    {
        var (dataLayer, repository, _, _) = DataLayer();

        repository.SaveFailure = new DbUpdateException("unique");
        repository.SlugExists = true;
        Assert.Equal(ArtistWriteOutcome.SlugTaken, await dataLayer.CreateAsync(new FakeArtistsDataLayer().Add(), "editor-1", Ct));

        // Any other database failure is a fault, not a refusal the editor could fix.
        repository.SaveFailure = new DbUpdateException("something else");
        repository.SlugExists = false;
        await Assert.ThrowsAsync<DbUpdateException>(() => dataLayer.CreateAsync(new FakeArtistsDataLayer().Add(), "editor-1", Ct));
    }

    // ---- Business ----

    [Fact]
    public async Task BusinessMapsASlugRaceAtTheSaveToSlugTaken()
    {
        var (business, dataLayer) = Business();
        dataLayer.Outcome = ArtistWriteOutcome.SlugTaken;

        var result = await business.CreateAsync(new ArtistCreateViewModel("Jane Doe"), "editor-1", Ct);

        Assert.Equal(ContentErrorCodes.SlugTaken, result.Error!.Code);
    }

    [Fact]
    public async Task BusinessMapsAConflictAtTheSaveToConcurrencyConflict()
    {
        var (business, dataLayer) = Business();
        var draft = dataLayer.Add();
        var published = dataLayer.Add(PublishStatus.Published, publishedAt: Now);
        dataLayer.Outcome = ArtistWriteOutcome.Conflict;

        var update = await business.UpdateAsync(draft.Id, new ArtistUpdateViewModel(Bio: "New"), EntityTag.Format(draft.RowVersion), null, "editor-1", Ct);
        var unpublish = await business.TransitionAsync(
            published.Id, PublishCommand.Unpublish, EntityTag.Format(published.RowVersion), null, "editor-1", Ct);

        Assert.Equal(ErrorCodes.ConcurrencyConflict, update.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, unpublish.Error!.Code);
    }

    [Fact]
    public async Task BusinessOwesARedirectOnlyForASlugThatWasEverPublic()
    {
        var (business, dataLayer) = Business();
        var draft = dataLayer.Add();
        var once = dataLayer.Add(PublishStatus.Draft, publishedAt: Now.AddDays(-2));

        await business.UpdateAsync(draft.Id, new ArtistUpdateViewModel(Slug: "jane-q"), EntityTag.Format(draft.RowVersion), null, "e", Ct);
        Assert.Null(dataLayer.LastPathChange);

        await business.UpdateAsync(once.Id, new ArtistUpdateViewModel(Slug: "jane-q"), EntityTag.Format(once.RowVersion), null, "e", Ct);
        Assert.Equal(new PublishedPathChange("/artists/jane-doe", "/artists/jane-q"), dataLayer.LastPathChange);
    }

    [Fact]
    public async Task BusinessChecksTheVersionBeforeAnyRule()
    {
        var (business, dataLayer) = Business();
        var artist = dataLayer.Add(PublishStatus.Archived);

        // Archived cannot be published, but a stale editor is told about the staleness first.
        var result = await business.TransitionAsync(
            artist.Id, PublishCommand.Publish, EntityTag.Format(Guid.NewGuid()), ArtistPortraitCheck.None, "editor-1", Ct);

        Assert.Equal(ErrorCodes.ConcurrencyConflict, result.Error!.Code);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task BusinessHandsTheDataLayerTheCommandAndTheStatusItLeft()
    {
        var (business, dataLayer) = Business();
        var artist = dataLayer.Add(PublishStatus.Published, publishedAt: Now.AddDays(-3));

        var result = await business.TransitionAsync(
            artist.Id, PublishCommand.Archive, EntityTag.Format(artist.RowVersion), null, "admin-1", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal((PublishCommand.Archive, PublishStatus.Published), dataLayer.LastTransition);
        Assert.Equal(PublishStatus.Archived, result.Value!.Status);
        Assert.Equal(Now.AddDays(-3), result.Value.PublishedAt);
    }

    [Fact]
    public async Task BusinessRefusesAPublishWhosePortraitCheckIsMissingOrForAnotherImage()
    {
        var (business, dataLayer) = Business();
        var portrait = Guid.NewGuid();
        var artist = dataLayer.Add(portrait: portrait);
        var etag = EntityTag.Format(artist.RowVersion);

        var noCheck = await business.TransitionAsync(artist.Id, PublishCommand.Publish, etag, null, "editor-1", Ct);
        var otherImage = await business.TransitionAsync(
            artist.Id, PublishCommand.Publish, etag, new ArtistPortraitCheck(Guid.NewGuid(), true, true), "editor-1", Ct);
        var gone = await business.TransitionAsync(
            artist.Id, PublishCommand.Publish, etag, new ArtistPortraitCheck(portrait, Exists: false, HasAltText: false), "editor-1", Ct);

        Assert.Equal(ErrorCodes.ConcurrencyConflict, noCheck.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, otherImage.Error!.Code);
        Assert.Equal(ErrorCodes.ValidationFailed, gone.Error!.Code);
        Assert.Contains("portraitMediaId", gone.Error.FieldErrors.Keys);
        Assert.Empty(dataLayer.Writes);
        Assert.Equal(PublishStatus.Draft, artist.Status);
    }

    [Fact]
    public async Task BusinessSetsTheFirstPublishTimeOnceAndClearsASchedule()
    {
        var (business, dataLayer) = Business();
        var artist = dataLayer.Add(PublishStatus.Scheduled);
        artist.ScheduledFor = Now.AddHours(1);

        var result = await business.TransitionAsync(
            artist.Id, PublishCommand.Publish, EntityTag.Format(artist.RowVersion), ArtistPortraitCheck.None, "editor-1", Ct);

        Assert.Equal(Now, result.Value!.PublishedAt);
        Assert.Null(result.Value.ScheduledFor);
        Assert.True(result.Value.SlugLocked);
    }

    [Fact]
    public async Task AnUnknownArtistIsNotFoundForEveryOperation()
    {
        var (business, _) = Business();
        var id = Guid.NewGuid();

        Assert.Equal(ErrorCodes.NotFound, (await business.GetAsync(id, Ct)).Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, (await business.UpdateAsync(id, new ArtistUpdateViewModel(Bio: "x"), "\"x\"", null, "e", Ct)).Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, (await business.TransitionAsync(id, PublishCommand.Publish, "\"x\"", null, "e", Ct)).Error!.Code);
    }

    [Fact]
    public async Task BusinessLetsADraftTakeAnAltLessPortraitButNotAPublishedArtist()
    {
        var (business, dataLayer) = Business();
        var draft = dataLayer.Add();
        var published = dataLayer.Add(PublishStatus.Published, publishedAt: Now);
        var image = Guid.NewGuid();
        var bare = new ArtistPortraitCheck(image, Exists: true, HasAltText: false);

        var onDraft = await business.UpdateAsync(
            draft.Id, new ArtistUpdateViewModel(PortraitMediaId: image), EntityTag.Format(draft.RowVersion), bare, "editor-1", Ct);
        var onPublished = await business.UpdateAsync(
            published.Id, new ArtistUpdateViewModel(PortraitMediaId: image), EntityTag.Format(published.RowVersion), bare, "editor-1", Ct);
        var described = await business.UpdateAsync(
            published.Id,
            new ArtistUpdateViewModel(PortraitMediaId: image),
            EntityTag.Format(published.RowVersion),
            bare with { HasAltText = true },
            "editor-1",
            Ct);

        Assert.True(onDraft.Succeeded);
        Assert.Equal(ErrorCodes.ValidationFailed, onPublished.Error!.Code);
        Assert.Contains("portraitMediaId", onPublished.Error.FieldErrors.Keys);
        Assert.True(described.Succeeded);
    }

    [Fact]
    public async Task TheSeedAddsOnlyTheSlugsNotInUseAsPublished()
    {
        var (business, dataLayer) = Business();
        dataLayer.Add();

        var added = await business.SeedAsync([("Jane Doe", null), ("Piloto", "Sculpture")], "system:test", Ct);

        Assert.Equal(1, added);
        var piloto = Assert.Single(dataLayer.Seeded);
        Assert.Equal(("piloto", PublishStatus.Published, Now), (piloto.Slug, piloto.Status, piloto.PublishedAt!.Value));
    }

    // ---- Facade ----

    [Fact]
    public async Task TheFacadeStopsAnInvalidRequestBeforeBusinessOrMedia()
    {
        var (facade, business, media, _) = Facade();

        var result = await facade.CreateAsync(new ArtistCreateViewModel(" ", PortraitMediaId: Guid.NewGuid()), "editor-1", Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Empty(business.Calls);
        Assert.Equal(0, media.Checks);
    }

    [Fact]
    public async Task TheFacadeRefusesAPortraitMediaDoesNotKnow()
    {
        var (facade, business, media, _) = Facade();
        var unknown = Guid.NewGuid();
        media.Missing.Add(unknown);

        var create = await facade.CreateAsync(new ArtistCreateViewModel("Jane", PortraitMediaId: unknown), "editor-1", Ct);
        var update = await facade.UpdateAsync(
            Guid.NewGuid(), new ArtistUpdateViewModel(PortraitMediaId: unknown), "\"0123456789abcdef0123456789abcdef\"", "editor-1", Ct);

        Assert.Contains("portraitMediaId", create.Error!.FieldErrors.Keys);
        Assert.Contains("portraitMediaId", update.Error!.FieldErrors.Keys);
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task TheFacadeLetsADraftTakeAPortraitWithoutAltText()
    {
        var (facade, business, media, _) = Facade();
        var bare = Guid.NewGuid();
        media.MissingAltText.Add(bare);

        var result = await facade.CreateAsync(new ArtistCreateViewModel("Jane", PortraitMediaId: bare), "editor-1", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal(["create"], business.Calls);
    }

    [Fact]
    public async Task PublishAsksMediaAboutTheArtistsCurrentPortraitAndPassesTheAnswerOn()
    {
        var (facade, business, media, _) = Facade();
        var portrait = Guid.NewGuid();
        business.Current = RecordingArtistsBusiness.Artist with { PortraitMediaId = portrait };
        media.MissingAltText.Add(portrait);

        await facade.PublishAsync(business.Current.Id, business.Current.ETag, "editor-1", Ct);

        Assert.Equal(["get", "transition:Publish"], business.Calls);
        Assert.Equal(new ArtistPortraitCheck(portrait, Exists: true, HasAltText: false), business.LastTransition!.Value.Portrait);
    }

    [Fact]
    public async Task PublishWithoutAPortraitDoesNotAskMediaAndUnpublishNeverDoes()
    {
        var (facade, business, media, _) = Facade();

        await facade.PublishAsync(business.Current.Id, business.Current.ETag, "editor-1", Ct);
        Assert.Equal(ArtistPortraitCheck.None, business.LastTransition!.Value.Portrait);

        await facade.UnpublishAsync(business.Current.Id, business.Current.ETag, "editor-1", Ct);
        await facade.ArchiveAsync(business.Current.Id, business.Current.ETag, "admin-1", Ct);

        Assert.Equal(0, media.Checks);
        Assert.Equal(["get", "transition:Publish", "transition:Unpublish", "transition:Archive"], business.Calls);
    }

    [Fact]
    public async Task PublishOfAnUnknownArtistStopsAtTheRead()
    {
        var (facade, business, _, _) = Facade();

        var result = await facade.PublishAsync(Guid.NewGuid(), "\"0123456789abcdef0123456789abcdef\"", "editor-1", Ct);

        Assert.Equal(ErrorCodes.NotFound, result.Error!.Code);
        Assert.Equal(["get"], business.Calls);
    }

    [Fact]
    public async Task AWriteWithoutIfMatchIsA428BeforeAnythingElseIsAsked()
    {
        // api-contract.md: no If-Match is a 428, even when the body is also wrong or names an unknown image.
        var (facade, business, media, _) = Facade();
        var unknown = Guid.NewGuid();
        media.Missing.Add(unknown);

        var update = await facade.UpdateAsync(Guid.NewGuid(), new ArtistUpdateViewModel(PortraitMediaId: unknown), null, "editor-1", Ct);
        var publish = await facade.PublishAsync(business.Current.Id, " ", "editor-1", Ct);
        var malformed = await facade.UpdateAsync(Guid.NewGuid(), new ArtistUpdateViewModel(), "*", "editor-1", Ct);

        Assert.Equal(ErrorCodes.PreconditionRequired, update.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, publish.Error!.Code);
        Assert.Equal(ErrorCodes.ValidationFailed, malformed.Error!.Code);
        Assert.Contains("ifMatch", malformed.Error.FieldErrors.Keys);
        Assert.Empty(business.Calls);
        Assert.Equal(0, media.Checks);
    }

    [Fact]
    public async Task AnEditNamingAPortraitHandsMediasAnswerToBusiness()
    {
        var (facade, business, media, _) = Facade();
        var bare = Guid.NewGuid();
        media.MissingAltText.Add(bare);

        await facade.UpdateAsync(
            Guid.NewGuid(), new ArtistUpdateViewModel(PortraitMediaId: bare), "\"0123456789abcdef0123456789abcdef\"", "editor-1", Ct);

        Assert.Equal(new ArtistPortraitCheck(bare, Exists: true, HasAltText: false), business.UpdatePortrait);
    }

    [Fact]
    public async Task AnEmptyLookupAsksTheDatabaseNothingAndARepeatedIdIsAskedOnce()
    {
        var (business, dataLayer) = Business();
        var jane = dataLayer.Add();

        var none = await business.GetSummariesAsync([], Ct);
        var once = await business.GetSummariesAsync([jane.Id, jane.Id], Ct);

        Assert.Empty(none);
        Assert.Single(once);
        Assert.Equal([jane.Id], Assert.Single(dataLayer.SummaryLookups));
    }

    [Fact]
    public async Task ALookupLargerThanThePolicyIsACallersBug()
    {
        var (facade, business, _, _) = Facade();
        var ids = Enumerable.Range(0, ArtistPolicy.LookupMaxCount + 1).Select(_ => Guid.NewGuid()).ToList();
        var slugs = ids.Select(id => id.ToString("N")).ToList();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => facade.GetSummariesAsync(ids, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => facade.FindBySlugsAsync(slugs, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => facade.FindByNamesAsync(slugs, Ct));
        Assert.Empty(business.Calls);
    }

    private static (ArtistsDataLayer, FakeArtistsRepository, RecordingAuditWriter, CallLog) DataLayer()
    {
        var log = new CallLog();
        var repository = new FakeArtistsRepository(log);
        var audit = new RecordingAuditWriter(log);
        return (new ArtistsDataLayer(repository, audit, new RecordingOutboxWriter(log), new FixedCorrelation(), new FixedClock(Now)), repository, audit, log);
    }

    private static (ArtistsBusiness, FakeArtistsDataLayer) Business()
    {
        var dataLayer = new FakeArtistsDataLayer();
        return (new ArtistsBusiness(dataLayer, new FixedClock(Now)), dataLayer);
    }

    private static (ArtistsFacade, RecordingArtistsBusiness, FakeMediaFacade, FakeContentReferences) Facade()
    {
        var business = new RecordingArtistsBusiness();
        var media = new FakeMediaFacade();
        var references = new FakeContentReferences();
        var facade = new ArtistsFacade(
            business, media, new ArtistCreateValidator(), new ArtistUpdateValidator(), new ArtistListValidator(),
            new ContentScheduleValidator(), references);
        return (facade, business, media, references);
    }
}
