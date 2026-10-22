using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Tests.Artists.Infrastructure;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Artists;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: the rules, the audit trail, the atomic
/// save, and the portrait's relationship with Media.
/// </summary>
public sealed class ArtistsModuleTests
{
    private const string Editor = ArtistsTestHost.EditorId;

    private const string Admin = ArtistsTestHost.AdminId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreatedArtistIsADraftWithASlugFromItsNameAndAnAuditRow()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);

        var artist = await CreateAsync(host, new ArtistCreateViewModel("  Socorro Muciño ", Medium: " Painting ", Bio: "One.\r\n\r\nTwo."));

        Assert.Equal("Socorro Muciño", artist.Name);
        Assert.Equal("socorro-mucino", artist.Slug);
        Assert.Equal("Painting", artist.Medium);
        Assert.Equal("One.\n\nTwo.", artist.Bio);
        Assert.Equal(PublishStatus.Draft, artist.Status);
        Assert.False(artist.SlugLocked);
        Assert.Null(artist.PublishedAt);
        Assert.Equal(Editor, artist.UpdatedBy);

        var stored = (await host.StoredAsync(artist.Id))!;
        Assert.Equal(EntityTag.Format(stored.RowVersion), artist.ETag);
        Assert.Equal(["artist.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ATakenSlugIsRefusedOnTheSlugField()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var second = await host.WithFacadeAsync(facade => facade.CreateAsync(new ArtistCreateViewModel("Jane  Doe"), Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, second.Error!.Code);
        Assert.Contains("slug", second.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task APortraitMustBeAnOriginalInTheLibraryButMayLackAltTextOnADraft()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var noAlt = await host.AddImageAsync(altText: null);
        var rendition = await host.AddImageAsync("A rendition", MediaAssetKind.Derived);

        var missing = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ArtistCreateViewModel("Jane Doe", PortraitMediaId: Guid.NewGuid()), Editor, Ct));
        var derived = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new ArtistCreateViewModel("Jane Doe", PortraitMediaId: rendition), Editor, Ct));
        var draft = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", PortraitMediaId: noAlt));

        Assert.Equal(ErrorCodes.ValidationFailed, missing.Error!.Code);
        Assert.Contains("portraitMediaId", missing.Error.FieldErrors.Keys);
        Assert.Equal(ErrorCodes.ValidationFailed, derived.Error!.Code);
        Assert.Equal(noAlt, draft.PortraitMediaId);
    }

    [Fact]
    public async Task PublishWaitsForThePortraitsAltText()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync(altText: null);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", PortraitMediaId: portrait));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Contains("portraitMediaId", refused.Error.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(artist.Id))!.Status);

        await SetAltTextAsync(host, portrait, "Jane in her studio");
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.Equal(host.Time.GetUtcNow(), published.PublishedAt);
        Assert.True(published.SlugLocked);
        Assert.NotEqual(artist.ETag, published.ETag);
        Assert.Contains("artist.published", await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AnArtistWithoutAPortraitPublishesWithoutAskingMedia()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
    }

    [Fact]
    public async Task TheLifecycleRunsDraftPublishedDraftArchivedAndKeepsTheFirstPublishTime()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));
        host.Time.Advance(TimeSpan.FromDays(1));
        var unpublished = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(artist.Id, published.ETag, Editor, Ct)));
        var republished = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, unpublished.ETag, Editor, Ct)));
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(artist.Id, republished.ETag, ArtistsTestHost.AdminId, Ct)));

        Assert.Equal(PublishStatus.Draft, unpublished.Status);
        Assert.Equal(published.PublishedAt, unpublished.PublishedAt);
        Assert.Equal(published.PublishedAt, republished.PublishedAt);
        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(ArtistsTestHost.AdminId, archived.UpdatedBy);
        Assert.Equal(
            ["artist.archived", "artist.created", "artist.published", "artist.published", "artist.unpublished"],
            (await host.AuditActionsAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task IllegalTransitionsAreRefusedAndChangeNothing()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var unpublishDraft = await host.WithFacadeAsync(facade => facade.UnpublishAsync(artist.Id, artist.ETag, Editor, Ct));
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, unpublishDraft.Error!.Code);

        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(artist.Id, artist.ETag, ArtistsTestHost.AdminId, Ct)));
        var publishArchived = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, archived.ETag, Editor, Ct));
        var archiveTwice = await host.WithFacadeAsync(facade => facade.ArchiveAsync(artist.Id, archived.ETag, ArtistsTestHost.AdminId, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, publishArchived.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, archiveTwice.Error!.Code);
        Assert.Equal(PublishStatus.Archived, (await host.StoredAsync(artist.Id))!.Status);
        Assert.Equal(EntityTag.Format((await host.StoredAsync(artist.Id))!.RowVersion), archived.ETag);
    }

    [Fact]
    public async Task PublishingTwiceIsRefusedRatherThanSilentlyIgnored()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        var again = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, published.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, again.Error!.Code);
    }

    [Fact]
    public async Task AStaleIfMatchIsAConflictAndTheRowIsUnchanged()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Medium: "Ceramics"), artist.ETag, Editor, Ct)));

        // A second editor still holding the first version.
        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Medium: "Painting"), artist.ETag, "editor-2", Ct));
        var stalePublish = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, "editor-2", Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stalePublish.Error!.Code);
        var stored = (await host.StoredAsync(artist.Id))!;
        Assert.Equal("Ceramics", stored.Medium);
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Equal(edited.ETag, EntityTag.Format(stored.RowVersion));
    }

    [Fact]
    public async Task AWriteWithoutIfMatchOrWithAMalformedOneIsRefused()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var missing = await host.WithFacadeAsync(facade => facade.UpdateAsync(artist.Id, new ArtistUpdateViewModel(Active: false), null, Editor, Ct));
        var weak = await host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, $"W/{artist.ETag}", Editor, Ct));

        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        Assert.Equal(ErrorCodes.ValidationFailed, weak.Error!.Code);
        Assert.Contains("ifMatch", weak.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task ASlugChangeOnceEverPublishedStagesARedirectInTheSameSave()
    {
        // ADR 0035 (2.7): a published slug may move; its old address is redirected, staged with the rename.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var moved = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-q-doe"), artist.ETag, Editor, Ct)));
        Assert.Empty(await StagedSlugChangesAsync(host));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, moved.ETag, Editor, Ct)));
        var unpublished = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(artist.Id, published.ETag, Editor, Ct)));
        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-doe"), unpublished.ETag, Editor, Ct)));

        Assert.Equal("jane-doe", renamed.Slug);
        var change = Assert.Single(await StagedSlugChangesAsync(host));
        Assert.Equal(("artist", artist.Id, "/artists/jane-q-doe", "/artists/jane-doe"), (change.ContentType, change.ContentId, change.FromPath, change.ToPath));
        Assert.Equal(renamed.UpdatedAt, change.OccurredAt);

        // Sending the slug it already has is not a change, so nothing more is staged.
        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-doe", Medium: "Ink"), renamed.ETag, Editor, Ct)));
        Assert.Single(await StagedSlugChangesAsync(host));
    }

    [Fact]
    public async Task AFailedRenameLeavesNeitherTheNewSlugNorItsRedirectMessage()
    {
        // outbox.md: the write and its message commit together or not at all (real database).
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ArtistsTestHost.CreateAsync(Ct, failAudit);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Slug: "jane-q"), published.ETag, Editor, Ct)));

        Assert.Equal("jane-doe", (await host.StoredAsync(artist.Id))!.Slug);
        Assert.Empty(await StagedSlugChangesAsync(host));
    }

    [Fact]
    public async Task APublishedArtistCannotSwapInAPortraitWithoutAltText()
    {
        // media.md: published content never carries an image without alt text, whichever way the image arrives.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var bare = await host.AddImageAsync(altText: null);
        var described = await host.AddImageAsync("Jane at the wheel");
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        var refused = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(PortraitMediaId: bare), published.ETag, Editor, Ct));
        var accepted = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(PortraitMediaId: described), published.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Contains("portraitMediaId", refused.Error.FieldErrors.Keys);
        Assert.Equal(described, accepted.Value!.PortraitMediaId);
    }

    [Fact]
    public async Task PublishStagesARevalidationInTheSameSave()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        var messages = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync(Ct));
        var message = Assert.Single(messages);
        Assert.Equal(SiteRevalidateRequested.MessageType, message.Type);

        // The payload names the artist and the address it lives at, and nothing else (ADR 0036).
        var staged = SiteRevalidateRequested.TryParse(message.PayloadJson)!;
        Assert.Equal("artist", staged.EntityType);
        Assert.Equal(artist.Id, staged.EntityId);
        Assert.Equal([$"/artists/{published.Slug}"], staged.Paths);
    }

    [Fact]
    public async Task EveryStatusCommandStagesItsOwnRevalidation()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));
        var drafted = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(artist.Id, published.ETag, Editor, Ct)));
        await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(artist.Id, drafted.ETag, Admin, Ct)));

        var types = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Select(message => message.Type).ToListAsync(Ct));

        Assert.Equal(3, types.Count);
        Assert.All(types, type => Assert.Equal(SiteRevalidateRequested.MessageType, type));
    }

    [Fact]
    public async Task AFailedStatusChangeLeavesNeitherTheStatusNorItsRevalidation()
    {
        // outbox.md's required atomicity test for the new message type, on a real database.
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ArtistsTestHost.CreateAsync(Ct, failAudit);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(
            facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(artist.Id))!.Status);
        Assert.Equal(0, await host.QueryAsync(context => context.OutboxMessages.CountAsync(Ct)));
    }

    [Fact]
    public async Task SchedulingSetsTheTimeAndUnschedulingClearsIt()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var when = host.Time.GetUtcNow().AddHours(2);

        var scheduled = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            artist.Id, new ContentScheduleViewModel(when), artist.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Scheduled, scheduled.Status);
        Assert.Equal(when, scheduled.ScheduledFor);
        Assert.Null(scheduled.PublishedAt);

        // Rescheduling a Scheduled artist moves the time rather than being refused as a no-op.
        var moved = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            artist.Id, new ContentScheduleViewModel(when.AddDays(1)), scheduled.ETag, Editor, Ct)));
        Assert.Equal(when.AddDays(1), moved.ScheduledFor);

        var drafted = await Ok(host.WithFacadeAsync(facade => facade.UnscheduleAsync(artist.Id, moved.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Draft, drafted.Status);
        Assert.Null(drafted.ScheduledFor);
        Assert.Equal(
            ["artist.created", "artist.scheduled", "artist.scheduled", "artist.unscheduled"],
            await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ATimeAlreadyPastIsRefusedOnTheField()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var result = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            artist.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddMinutes(-1)), artist.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("scheduledFor", result.Error.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(artist.Id))!.Status);
    }

    [Fact]
    public async Task SchedulingARecordThatCouldNotBePublishedIsRefusedNowRatherThanLater()
    {
        // ADR 0036: the publish requirements are checked at schedule too, so a missing alt text is a refusal on
        // the screen rather than an abandoned schedule at midnight.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var bare = await host.AddImageAsync(altText: null);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", PortraitMediaId: bare));

        var result = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            artist.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddHours(1)), artist.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("portraitMediaId", result.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task SchedulingAPublishedArtistAndUnschedulingADraftAreBothRefused()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));
        var draft = await CreateAsync(host, new ArtistCreateViewModel("John Doe"));

        var schedulePublished = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            artist.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddHours(1)), published.ETag, Editor, Ct));
        var unscheduleDraft = await host.WithFacadeAsync(facade => facade.UnscheduleAsync(draft.Id, draft.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, schedulePublished.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, unscheduleDraft.Error!.Code);
    }

    [Fact]
    public async Task AStatusCommandWithoutAnIfMatchIsRefusedBeforeAnythingIsRead()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var schedule = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            artist.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddHours(1)), ifMatch: null, Editor, Ct));
        var unschedule = await host.WithFacadeAsync(facade => facade.UnscheduleAsync(artist.Id, ifMatch: null, Editor, Ct));

        Assert.Equal(ErrorCodes.PreconditionRequired, schedule.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, unschedule.Error!.Code);
    }

    [Fact]
    public async Task TheScheduledPublisherPublishesWhatIsDueAndSkipsWhatIsNot()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var due = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var later = await CreateAsync(host, new ArtistCreateViewModel("John Doe"));
        var asOf = host.Time.GetUtcNow();
        await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            due.Id, new ContentScheduleViewModel(asOf.AddMinutes(1)), due.ETag, Editor, Ct)));
        await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            later.Id, new ContentScheduleViewModel(asOf.AddHours(3)), later.ETag, Editor, Ct)));

        // The pass runs at the first artist's time: the second is not due yet.
        var passTime = asOf.AddMinutes(2);
        var ids = await host.WithFacadeAsync(facade => facade.ListDueAsync(passTime, 50, Ct));
        var outcome = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(due.Id, passTime, Ct));
        var notDue = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(later.Id, passTime, Ct));

        Assert.Equal([due.Id], ids);
        Assert.Equal(ScheduledPublishOutcome.Published, outcome);
        Assert.Equal(ScheduledPublishOutcome.Skipped, notDue);

        var stored = (await host.StoredAsync(due.Id))!;
        Assert.Equal(PublishStatus.Published, stored.Status);
        Assert.Null(stored.ScheduledFor);
        Assert.Equal(ScheduledPublishPolicy.Actor, stored.UpdatedBy);
        Assert.Equal(PublishStatus.Scheduled, (await host.StoredAsync(later.Id))!.Status);
    }

    [Fact]
    public async Task ADueArtistThatCanNoLongerBePublishedBecomesADraftWithAnAuditRow()
    {
        // ADR 0036: rather than retrying every minute for ever with nobody told.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var described = await host.AddImageAsync("Jane at the wheel");
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", PortraitMediaId: described));
        var asOf = host.Time.GetUtcNow();
        await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            artist.Id, new ContentScheduleViewModel(asOf.AddMinutes(1)), artist.ETag, Editor, Ct)));

        // The portrait loses its alt text while the artist waits.
        await host.ExecuteAsync(async context =>
        {
            var asset = await context.Set<MediaAsset>().SingleAsync(image => image.Id == described, Ct);
            asset.AltText = null;
            await context.SaveChangesAsync(Ct);
        });

        var outcome = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(artist.Id, asOf.AddMinutes(2), Ct));

        Assert.Equal(ScheduledPublishOutcome.Unscheduled, outcome);
        var stored = (await host.StoredAsync(artist.Id))!;
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Null(stored.ScheduledFor);
        Assert.Contains("artist.schedule-abandoned", await host.AuditActionsAsync());

        // Giving up is a status change like any other, so it stages its own revalidation in the same save: the
        // schedule's message and this one (ADR 0036).
        var staged = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == SiteRevalidateRequested.MessageType)
            .Select(message => message.PayloadJson)
            .ToListAsync(Ct));

        Assert.Equal(2, staged.Count);
        Assert.All(staged, payload =>
        {
            var parsed = SiteRevalidateRequested.TryParse(payload)!;
            Assert.Equal(("artist", artist.Id), (parsed.EntityType, parsed.EntityId));
            Assert.Equal(["/artists/jane-doe"], parsed.Paths);
        });
    }

    [Fact]
    public async Task AnEditCannotTakeAnotherArtistsSlug()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var john = await CreateAsync(host, new ArtistCreateViewModel("John Doe"));

        var result = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            john.Id, new ArtistUpdateViewModel(Slug: "jane-doe"), john.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, result.Error!.Code);
    }

    [Fact]
    public async Task AnEditClearsSetsAndRemovesAndAnEmptyEditWritesNothing()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync("Jane");
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", Medium: "Ink", Bio: "Bio", PortraitMediaId: portrait));

        var cleared = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Medium: "", Bio: "  ", Active: false, RemovePortrait: true), artist.ETag, Editor, Ct)));

        Assert.Null(cleared.Medium);
        Assert.Null(cleared.Bio);
        Assert.False(cleared.Active);
        Assert.Null(cleared.PortraitMediaId);

        var unchanged = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(Name: "Jane Doe ", Active: false), cleared.ETag, Editor, Ct)));

        Assert.Equal(cleared.ETag, unchanged.ETag);
        Assert.Equal(2, (await host.AuditActionsAsync()).Count);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheArtistNorItsAuditRow()
    {
        // backend.md: the real-database test that the write and its bookkeeping commit together or not at all.
        var failAudit = new FailInsertInto("AuditLogs");
        await using var host = await ArtistsTestHost.CreateAsync(Ct, failAudit);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade =>
            facade.CreateAsync(new ArtistCreateViewModel("Jane Doe"), Editor, Ct)));

        Assert.True(failAudit.Fired);
        Assert.Equal(0, await host.QueryAsync(context => context.Set<Artist>().CountAsync(Ct)));
        Assert.Empty(await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AFailedTransitionLeavesTheStatusAndVersionAsTheyWere()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await ArtistsTestHost.CreateAsync(Ct, failAudit);
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.PublishAsync(artist.Id, artist.ETag, Editor, Ct)));

        var stored = (await host.StoredAsync(artist.Id))!;
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Null(stored.PublishedAt);
        Assert.Equal(artist.ETag, EntityTag.Format(stored.RowVersion));
        Assert.Equal(["artist.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task MediaRefusesToDeleteAPortraitInUseAndAllowsItOnceRemoved()
    {
        // The first real Restrict foreign key to MediaAsset: Media's delete guard finds it in the model (ADR 0030).
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync("Jane");
        var artist = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe", PortraitMediaId: portrait));

        var inUse = await DeleteImageAsync(host, portrait);
        Assert.Equal(MediaErrorCodes.InUse, inUse.Error!.Code);

        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            artist.Id, new ArtistUpdateViewModel(RemovePortrait: true), artist.ETag, Editor, Ct)));

        Assert.True((await DeleteImageAsync(host, portrait)).Succeeded);
    }

    [Fact]
    public async Task TheListPagesByCursorAndACursorOnlyFitsItsOwnFilter()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        foreach (var name in new[] { "Ann", "Ben", "Cal" })
        {
            await CreateAsync(host, new ArtistCreateViewModel(name));
        }

        var first = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ArtistListViewModel(Limit: 2), Ct)));
        var second = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ArtistListViewModel(first.NextCursor, Limit: 2), Ct)));
        var otherFilter = await host.WithFacadeAsync(facade => facade.ListAsync(
            new ArtistListViewModel(first.NextCursor, Limit: 2, Status: PublishStatus.Draft), Ct));

        Assert.Equal(["Ann", "Ben"], first.Items.Select(row => row.Name));
        Assert.Equal(["Cal"], second.Items.Select(row => row.Name));
        Assert.Null(second.NextCursor);
        Assert.Equal(ErrorCodes.CursorInvalid, otherFilter.Error!.Code);
    }

    [Fact]
    public async Task TheDevelopmentSeedAddsTheSpikeRosterOnceAndNeverUndoesAnEdit()
    {
        await using var host = await ArtistsTestHost.CreateAsync(Ct, withSeeder: true);

        await host.SeedAsync(Ct);
        var carrie = await host.QueryAsync(context => context.Set<Artist>().AsNoTracking().SingleAsync(a => a.Slug == "carrie-haase", Ct));
        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            carrie.Id, new ArtistUpdateViewModel(Medium: "Metalsmithing"), EntityTag.Format(carrie.RowVersion), Editor, Ct)));
        await host.SeedAsync(Ct);

        var all = await host.QueryAsync(context => context.Set<Artist>().AsNoTracking().ToListAsync(Ct));
        Assert.Equal(ArtistDevelopmentSeeder.Roster.Count, all.Count);
        Assert.All(all, artist => Assert.Equal(PublishStatus.Published, artist.Status));
        Assert.Equal("Jewelry", carrie.Medium);
        Assert.Equal("Metalsmithing", all.Single(artist => artist.Slug == "carrie-haase").Medium);
        Assert.Equal(ArtistDevelopmentSeeder.SeedActor, all.First(artist => artist.Slug == "piloto").UpdatedBy);
        Assert.Single(await host.AuditActionsAsync(), action => action == "artist.seeded");
    }

    [Fact]
    public async Task AnotherModuleLooksArtistsUpByIdOrSlugWhateverTheirStatus()
    {
        // Exhibitions' credits (ADR 0033): every artist that exists comes back, archived ones included, and an id or
        // slug with no artist is simply absent.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var jane = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var john = await CreateAsync(host, new ArtistCreateViewModel("John Doe"));
        await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(john.Id, john.ETag, ArtistsTestHost.AdminId, Ct)));

        var byId = await host.WithFacadeAsync(facade => facade.GetSummariesAsync([jane.Id, john.Id, Guid.NewGuid(), jane.Id], Ct));
        var bySlug = await host.WithFacadeAsync(facade => facade.FindBySlugsAsync(["john-doe", "nobody"], Ct));
        var none = await host.WithFacadeAsync(facade => facade.GetSummariesAsync([], Ct));

        Assert.Equal(["Jane Doe", "John Doe"], byId.Select(artist => artist.Name).Order(StringComparer.Ordinal));
        Assert.Equal(PublishStatus.Archived, byId.Single(artist => artist.Id == john.Id).Status);
        Assert.Equal(john.Id, Assert.Single(bySlug).Id);
        Assert.Empty(none);
    }

    [Fact]
    public async Task TheShopImportLooksArtistsUpByNameWhateverTheCase()
    {
        // ADR 0053: staff type a name into a spreadsheet. The match ignores case on SQLite as under SQL Server's
        // collation, archived artists come back for the caller to refuse, and a name nobody has is simply absent.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var jane = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        var john = await CreateAsync(host, new ArtistCreateViewModel("John Doe"));
        await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(john.Id, john.ETag, ArtistsTestHost.AdminId, Ct)));

        var found = await host.WithFacadeAsync(facade => facade.FindByNamesAsync(["JANE DOE", " john doe ", "Nobody", ""], Ct));
        var none = await host.WithFacadeAsync(facade => facade.FindByNamesAsync([" "], Ct));

        Assert.Equal(new[] { jane.Id, john.Id }.Order(), found.Select(artist => artist.Id).Order());
        Assert.Equal(PublishStatus.Archived, found.Single(artist => artist.Id == john.Id).Status);
        Assert.Empty(none);
    }

    [Fact]
    public async Task ADeletedRecordIsGoneFromEveryReadAndARestoreBringsItBackAsADraft()
    {
        // ADR 0052: one query filter, so "deleted" means gone from every read — and a restore is a decision to
        // look at the record again, not to publish it.
        await using var host = await ArtistsTestHost.CreateAsync(Ct);
        var created = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));

        var deleted = await host.WithFacadeAsync(facade => facade.DeleteAsync(created.Id, created.ETag, ArtistsTestHost.AdminId, Ct));

        Assert.True(deleted.Succeeded, deleted.Error?.Code);
        Assert.Equal(ErrorCodes.NotFound, (await host.WithFacadeAsync(facade => facade.GetAsync(created.Id, Ct))).Error!.Code);
        Assert.Empty((await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new ArtistListViewModel(), Ct)))).Items);
        Assert.Null(await host.StoredAsync(created.Id));

        // The address is free at once, which is the point: the mistake stops standing in the way.
        var replacement = await CreateAsync(host, new ArtistCreateViewModel("Jane Doe"));
        Assert.Equal("jane-doe", replacement.Slug);

        var trash = await Ok(host.WithFacadeAsync(facade => facade.ListDeletedAsync(new ArtistListViewModel(), Ct)));
        var row = Assert.Single(trash.Items);
        Assert.Equal("Jane Doe", row.Name);

        // The replacement holds the address now, so the restore is refused until it does not.
        var refused = await host.WithFacadeAsync(facade => facade.RestoreAsync(created.Id, row.ETag, ArtistsTestHost.AdminId, Ct));
        Assert.Equal(ContentErrorCodes.SlugTaken, refused.Error!.Code);

        await host.WithFacadeAsync(facade => facade.DeleteAsync(replacement.Id, replacement.ETag, ArtistsTestHost.AdminId, Ct));
        var restored = await Ok(host.WithFacadeAsync(facade => facade.RestoreAsync(created.Id, row.ETag, ArtistsTestHost.AdminId, Ct)));

        Assert.Equal(PublishStatus.Draft, restored.Status);
        Assert.Null(restored.ScheduledFor);
        Assert.Contains("artist.deleted", await host.AuditActionsAsync());
        Assert.Contains("artist.restored", await host.AuditActionsAsync());
    }

    private static async Task<List<RedirectSlugChanged>> StagedSlugChangesAsync(ArtistsTestHost host) =>
        [.. (await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
                .Where(message => message.Type == RedirectSlugChanged.MessageType).Select(message => message.PayloadJson).ToListAsync(Ct)))
            .Select(payload => RedirectSlugChanged.TryParse(payload)!)];

    private static async Task<ArtistServiceModel> CreateAsync(ArtistsTestHost host, ArtistCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }

    private static Task SetAltTextAsync(ArtistsTestHost host, Guid assetId, string altText) =>
        host.ExecuteAsync(async context =>
        {
            var asset = await context.Set<MediaAsset>().SingleAsync(row => row.Id == assetId, Ct);
            asset.AltText = altText;
            await context.SaveChangesAsync(Ct);
        });

    private static async Task<OperationResult> DeleteImageAsync(ArtistsTestHost host, Guid assetId)
    {
        var version = await host.QueryAsync(context =>
            context.Set<MediaAsset>().AsNoTracking().Where(row => row.Id == assetId).Select(row => row.RowVersion).SingleAsync(Ct));

        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaFacade>()
            .DeleteAsync(assetId, EntityTag.Format(version), "admin-1", Ct);
    }
}
