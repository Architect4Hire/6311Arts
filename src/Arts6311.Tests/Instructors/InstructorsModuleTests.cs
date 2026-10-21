using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ViewModels;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Tests.Instructors.Infrastructure;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Instructors;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: the rules, the audit trail, the atomic
/// save, and the portrait's relationship with Media.
/// </summary>
public sealed class InstructorsModuleTests
{
    private const string Editor = InstructorsTestHost.EditorId;

    private const string Admin = InstructorsTestHost.AdminId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreatedInstructorIsADraftWithASlugFromItsNameAndAnAuditRow()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);

        var instructor = await CreateAsync(host, new InstructorCreateViewModel("  Socorro Muciño ", Medium: " Painting ", Bio: "One.\r\n\r\nTwo."));

        Assert.Equal("Socorro Muciño", instructor.Name);
        Assert.Equal("socorro-mucino", instructor.Slug);
        Assert.Equal("Painting", instructor.Medium);
        Assert.Equal("One.\n\nTwo.", instructor.Bio);
        Assert.Equal(PublishStatus.Draft, instructor.Status);
        Assert.False(instructor.SlugLocked);
        Assert.Null(instructor.PublishedAt);
        Assert.Equal(Editor, instructor.UpdatedBy);

        var stored = (await host.StoredAsync(instructor.Id))!;
        Assert.Equal(EntityTag.Format(stored.RowVersion), instructor.ETag);
        Assert.Equal(["instructor.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ATakenSlugIsRefusedOnTheSlugField()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var second = await host.WithFacadeAsync(facade => facade.CreateAsync(new InstructorCreateViewModel("Jane  Doe"), Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, second.Error!.Code);
        Assert.Contains("slug", second.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task APortraitMustBeAnOriginalInTheLibraryButMayLackAltTextOnADraft()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var noAlt = await host.AddImageAsync(altText: null);
        var rendition = await host.AddImageAsync("A rendition", MediaAssetKind.Derived);

        var missing = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new InstructorCreateViewModel("Jane Doe", PortraitMediaId: Guid.NewGuid()), Editor, Ct));
        var derived = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new InstructorCreateViewModel("Jane Doe", PortraitMediaId: rendition), Editor, Ct));
        var draft = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe", PortraitMediaId: noAlt));

        Assert.Equal(ErrorCodes.ValidationFailed, missing.Error!.Code);
        Assert.Contains("portraitMediaId", missing.Error.FieldErrors.Keys);
        Assert.Equal(ErrorCodes.ValidationFailed, derived.Error!.Code);
        Assert.Equal(noAlt, draft.PortraitMediaId);
    }

    [Fact]
    public async Task PublishWaitsForThePortraitsAltText()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync(altText: null);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe", PortraitMediaId: portrait));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Contains("portraitMediaId", refused.Error.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(instructor.Id))!.Status);

        await SetAltTextAsync(host, portrait, "Jane in her studio");
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.Equal(host.Time.GetUtcNow(), published.PublishedAt);
        Assert.True(published.SlugLocked);
        Assert.NotEqual(instructor.ETag, published.ETag);
        Assert.Contains("instructor.published", await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AnInstructorWithoutAPortraitPublishesWithoutAskingMedia()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Published, published.Status);
    }

    [Fact]
    public async Task TheLifecycleRunsDraftPublishedDraftArchivedAndKeepsTheFirstPublishTime()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));
        host.Time.Advance(TimeSpan.FromDays(1));
        var unpublished = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(instructor.Id, published.ETag, Editor, Ct)));
        var republished = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, unpublished.ETag, Editor, Ct)));
        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(instructor.Id, republished.ETag, InstructorsTestHost.AdminId, Ct)));

        Assert.Equal(PublishStatus.Draft, unpublished.Status);
        Assert.Equal(published.PublishedAt, unpublished.PublishedAt);
        Assert.Equal(published.PublishedAt, republished.PublishedAt);
        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(InstructorsTestHost.AdminId, archived.UpdatedBy);
        Assert.Equal(
            ["instructor.archived", "instructor.created", "instructor.published", "instructor.published", "instructor.unpublished"],
            (await host.AuditActionsAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task IllegalTransitionsAreRefusedAndChangeNothing()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var unpublishDraft = await host.WithFacadeAsync(facade => facade.UnpublishAsync(instructor.Id, instructor.ETag, Editor, Ct));
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, unpublishDraft.Error!.Code);

        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(instructor.Id, instructor.ETag, InstructorsTestHost.AdminId, Ct)));
        var publishArchived = await host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, archived.ETag, Editor, Ct));
        var archiveTwice = await host.WithFacadeAsync(facade => facade.ArchiveAsync(instructor.Id, archived.ETag, InstructorsTestHost.AdminId, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, publishArchived.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, archiveTwice.Error!.Code);
        Assert.Equal(PublishStatus.Archived, (await host.StoredAsync(instructor.Id))!.Status);
        Assert.Equal(EntityTag.Format((await host.StoredAsync(instructor.Id))!.RowVersion), archived.ETag);
    }

    [Fact]
    public async Task PublishingTwiceIsRefusedRatherThanSilentlyIgnored()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));

        var again = await host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, published.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, again.Error!.Code);
    }

    [Fact]
    public async Task AStaleIfMatchIsAConflictAndTheRowIsUnchanged()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var edited = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Medium: "Ceramics"), instructor.ETag, Editor, Ct)));

        // A second editor still holding the first version.
        var stale = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Medium: "Painting"), instructor.ETag, "editor-2", Ct));
        var stalePublish = await host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, "editor-2", Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stalePublish.Error!.Code);
        var stored = (await host.StoredAsync(instructor.Id))!;
        Assert.Equal("Ceramics", stored.Medium);
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Equal(edited.ETag, EntityTag.Format(stored.RowVersion));
    }

    [Fact]
    public async Task AWriteWithoutIfMatchOrWithAMalformedOneIsRefused()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var missing = await host.WithFacadeAsync(facade => facade.UpdateAsync(instructor.Id, new InstructorUpdateViewModel(Active: false), null, Editor, Ct));
        var weak = await host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, $"W/{instructor.ETag}", Editor, Ct));

        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        Assert.Equal(ErrorCodes.ValidationFailed, weak.Error!.Code);
        Assert.Contains("ifMatch", weak.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task ASlugChangeOnceEverPublishedStagesARedirectInTheSameSave()
    {
        // ADR 0035 (2.7): a published slug may move; its old address is redirected, staged with the rename.
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var moved = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Slug: "jane-q-doe"), instructor.ETag, Editor, Ct)));
        Assert.Empty(await StagedSlugChangesAsync(host));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, moved.ETag, Editor, Ct)));
        var unpublished = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(instructor.Id, published.ETag, Editor, Ct)));
        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Slug: "jane-doe"), unpublished.ETag, Editor, Ct)));

        Assert.Equal("jane-doe", renamed.Slug);
        var change = Assert.Single(await StagedSlugChangesAsync(host));
        Assert.Equal(("instructor", instructor.Id, "/instructors/jane-q-doe", "/instructors/jane-doe"), (change.ContentType, change.ContentId, change.FromPath, change.ToPath));
        Assert.Equal(renamed.UpdatedAt, change.OccurredAt);

        // Sending the slug it already has is not a change, so nothing more is staged.
        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Slug: "jane-doe", Medium: "Ink"), renamed.ETag, Editor, Ct)));
        Assert.Single(await StagedSlugChangesAsync(host));
    }

    [Fact]
    public async Task AFailedRenameLeavesNeitherTheNewSlugNorItsRedirectMessage()
    {
        // outbox.md: the write and its message commit together or not at all (real database).
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await InstructorsTestHost.CreateAsync(Ct, failAudit);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Slug: "jane-q"), published.ETag, Editor, Ct)));

        Assert.Equal("jane-doe", (await host.StoredAsync(instructor.Id))!.Slug);
        Assert.Empty(await StagedSlugChangesAsync(host));
    }

    [Fact]
    public async Task APublishedInstructorCannotSwapInAPortraitWithoutAltText()
    {
        // media.md: published content never carries an image without alt text, whichever way the image arrives.
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var bare = await host.AddImageAsync(altText: null);
        var described = await host.AddImageAsync("Jane at the wheel");
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));

        var refused = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(PortraitMediaId: bare), published.ETag, Editor, Ct));
        var accepted = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(PortraitMediaId: described), published.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Contains("portraitMediaId", refused.Error.FieldErrors.Keys);
        Assert.Equal(described, accepted.Value!.PortraitMediaId);
    }

    [Fact]
    public async Task PublishStagesARevalidationInTheSameSave()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));

        var messages = await host.QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync(Ct));
        var message = Assert.Single(messages);
        Assert.Equal(SiteRevalidateRequested.MessageType, message.Type);

        // The payload names the instructor and the address it lives at, and nothing else (ADR 0036).
        var staged = SiteRevalidateRequested.TryParse(message.PayloadJson)!;
        Assert.Equal("instructor", staged.EntityType);
        Assert.Equal(instructor.Id, staged.EntityId);
        Assert.Equal([$"/instructors/{published.Slug}"], staged.Paths);
    }

    [Fact]
    public async Task EveryStatusCommandStagesItsOwnRevalidation()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));
        var drafted = await Ok(host.WithFacadeAsync(facade => facade.UnpublishAsync(instructor.Id, published.ETag, Editor, Ct)));
        await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(instructor.Id, drafted.ETag, Admin, Ct)));

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
        await using var host = await InstructorsTestHost.CreateAsync(Ct, failAudit);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(
            facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(instructor.Id))!.Status);
        Assert.Equal(0, await host.QueryAsync(context => context.OutboxMessages.CountAsync(Ct)));
    }

    [Fact]
    public async Task SchedulingSetsTheTimeAndUnschedulingClearsIt()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var when = host.Time.GetUtcNow().AddHours(2);

        var scheduled = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            instructor.Id, new ContentScheduleViewModel(when), instructor.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Scheduled, scheduled.Status);
        Assert.Equal(when, scheduled.ScheduledFor);
        Assert.Null(scheduled.PublishedAt);

        // Rescheduling a Scheduled instructor moves the time rather than being refused as a no-op.
        var moved = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            instructor.Id, new ContentScheduleViewModel(when.AddDays(1)), scheduled.ETag, Editor, Ct)));
        Assert.Equal(when.AddDays(1), moved.ScheduledFor);

        var drafted = await Ok(host.WithFacadeAsync(facade => facade.UnscheduleAsync(instructor.Id, moved.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Draft, drafted.Status);
        Assert.Null(drafted.ScheduledFor);
        Assert.Equal(
            ["instructor.created", "instructor.scheduled", "instructor.scheduled", "instructor.unscheduled"],
            await host.AuditActionsAsync());
    }

    [Fact]
    public async Task ATimeAlreadyPastIsRefusedOnTheField()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var result = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            instructor.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddMinutes(-1)), instructor.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("scheduledFor", result.Error.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(instructor.Id))!.Status);
    }

    [Fact]
    public async Task SchedulingARecordThatCouldNotBePublishedIsRefusedNowRatherThanLater()
    {
        // ADR 0036: the publish requirements are checked at schedule too, so a missing alt text is a refusal on
        // the screen rather than an abandoned schedule at midnight.
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var bare = await host.AddImageAsync(altText: null);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe", PortraitMediaId: bare));

        var result = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            instructor.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddHours(1)), instructor.ETag, Editor, Ct));

        Assert.Equal(ErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Contains("portraitMediaId", result.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task SchedulingAPublishedInstructorAndUnschedulingADraftAreBothRefused()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));
        var draft = await CreateAsync(host, new InstructorCreateViewModel("John Doe"));

        var schedulePublished = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            instructor.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddHours(1)), published.ETag, Editor, Ct));
        var unscheduleDraft = await host.WithFacadeAsync(facade => facade.UnscheduleAsync(draft.Id, draft.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, schedulePublished.Error!.Code);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, unscheduleDraft.Error!.Code);
    }

    [Fact]
    public async Task AStatusCommandWithoutAnIfMatchIsRefusedBeforeAnythingIsRead()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        var schedule = await host.WithFacadeAsync(facade => facade.ScheduleAsync(
            instructor.Id, new ContentScheduleViewModel(host.Time.GetUtcNow().AddHours(1)), ifMatch: null, Editor, Ct));
        var unschedule = await host.WithFacadeAsync(facade => facade.UnscheduleAsync(instructor.Id, ifMatch: null, Editor, Ct));

        Assert.Equal(ErrorCodes.PreconditionRequired, schedule.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, unschedule.Error!.Code);
    }

    [Fact]
    public async Task TheScheduledPublisherPublishesWhatIsDueAndSkipsWhatIsNot()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var due = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var later = await CreateAsync(host, new InstructorCreateViewModel("John Doe"));
        var asOf = host.Time.GetUtcNow();
        await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            due.Id, new ContentScheduleViewModel(asOf.AddMinutes(1)), due.ETag, Editor, Ct)));
        await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            later.Id, new ContentScheduleViewModel(asOf.AddHours(3)), later.ETag, Editor, Ct)));

        // The pass runs at the first instructor's time: the second is not due yet.
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
    public async Task ADueInstructorThatCanNoLongerBePublishedBecomesADraftWithAnAuditRow()
    {
        // ADR 0036: rather than retrying every minute for ever with nobody told.
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var described = await host.AddImageAsync("Jane at the wheel");
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe", PortraitMediaId: described));
        var asOf = host.Time.GetUtcNow();
        await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            instructor.Id, new ContentScheduleViewModel(asOf.AddMinutes(1)), instructor.ETag, Editor, Ct)));

        // The portrait loses its alt text while the instructor waits.
        await host.ExecuteAsync(async context =>
        {
            var asset = await context.Set<MediaAsset>().SingleAsync(image => image.Id == described, Ct);
            asset.AltText = null;
            await context.SaveChangesAsync(Ct);
        });

        var outcome = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(instructor.Id, asOf.AddMinutes(2), Ct));

        Assert.Equal(ScheduledPublishOutcome.Unscheduled, outcome);
        var stored = (await host.StoredAsync(instructor.Id))!;
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Null(stored.ScheduledFor);
        Assert.Contains("instructor.schedule-abandoned", await host.AuditActionsAsync());

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
            Assert.Equal(("instructor", instructor.Id), (parsed.EntityType, parsed.EntityId));
            Assert.Equal(["/instructors/jane-doe"], parsed.Paths);
        });
    }

    [Fact]
    public async Task AnEditCannotTakeAnotherInstructorsSlug()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var john = await CreateAsync(host, new InstructorCreateViewModel("John Doe"));

        var result = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            john.Id, new InstructorUpdateViewModel(Slug: "jane-doe"), john.ETag, Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, result.Error!.Code);
    }

    [Fact]
    public async Task AnEditClearsSetsAndRemovesAndAnEmptyEditWritesNothing()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync("Jane");
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe", Medium: "Ink", Bio: "Bio", PortraitMediaId: portrait));

        var cleared = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Medium: "", Bio: "  ", Active: false, RemovePortrait: true), instructor.ETag, Editor, Ct)));

        Assert.Null(cleared.Medium);
        Assert.Null(cleared.Bio);
        Assert.False(cleared.Active);
        Assert.Null(cleared.PortraitMediaId);

        var unchanged = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Name: "Jane Doe ", Active: false), cleared.ETag, Editor, Ct)));

        Assert.Equal(cleared.ETag, unchanged.ETag);
        Assert.Equal(2, (await host.AuditActionsAsync()).Count);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheInstructorNorItsAuditRow()
    {
        // backend.md: the real-database test that the write and its bookkeeping commit together or not at all.
        var failAudit = new FailInsertInto("AuditLogs");
        await using var host = await InstructorsTestHost.CreateAsync(Ct, failAudit);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade =>
            facade.CreateAsync(new InstructorCreateViewModel("Jane Doe"), Editor, Ct)));

        Assert.True(failAudit.Fired);
        Assert.Equal(0, await host.QueryAsync(context => context.Set<Instructor>().CountAsync(Ct)));
        Assert.Empty(await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AFailedTransitionLeavesTheStatusAndVersionAsTheyWere()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await InstructorsTestHost.CreateAsync(Ct, failAudit);
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.PublishAsync(instructor.Id, instructor.ETag, Editor, Ct)));

        var stored = (await host.StoredAsync(instructor.Id))!;
        Assert.Equal(PublishStatus.Draft, stored.Status);
        Assert.Null(stored.PublishedAt);
        Assert.Equal(instructor.ETag, EntityTag.Format(stored.RowVersion));
        Assert.Equal(["instructor.created"], await host.AuditActionsAsync());
    }

    [Fact]
    public async Task MediaRefusesToDeleteAPortraitInUseAndAllowsItOnceRemoved()
    {
        // The first real Restrict foreign key to MediaAsset: Media's delete guard finds it in the model (ADR 0030).
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var portrait = await host.AddImageAsync("Jane");
        var instructor = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe", PortraitMediaId: portrait));

        var inUse = await DeleteImageAsync(host, portrait);
        Assert.Equal(MediaErrorCodes.InUse, inUse.Error!.Code);

        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(RemovePortrait: true), instructor.ETag, Editor, Ct)));

        Assert.True((await DeleteImageAsync(host, portrait)).Succeeded);
    }

    [Fact]
    public async Task TheListPagesByCursorAndACursorOnlyFitsItsOwnFilter()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        foreach (var name in new[] { "Ann", "Ben", "Cal" })
        {
            await CreateAsync(host, new InstructorCreateViewModel(name));
        }

        var first = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new InstructorListViewModel(Limit: 2), Ct)));
        var second = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new InstructorListViewModel(first.NextCursor, Limit: 2), Ct)));
        var otherFilter = await host.WithFacadeAsync(facade => facade.ListAsync(
            new InstructorListViewModel(first.NextCursor, Limit: 2, Status: PublishStatus.Draft), Ct));

        Assert.Equal(["Ann", "Ben"], first.Items.Select(row => row.Name));
        Assert.Equal(["Cal"], second.Items.Select(row => row.Name));
        Assert.Null(second.NextCursor);
        Assert.Equal(ErrorCodes.CursorInvalid, otherFilter.Error!.Code);
    }

    [Fact]
    public async Task TheDevelopmentSeedAddsTheSpikeRosterOnceAndNeverUndoesAnEdit()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct, withSeeder: true);

        await host.SeedAsync(Ct);
        var gwen = await host.QueryAsync(context => context.Set<Instructor>().AsNoTracking().SingleAsync(a => a.Slug == "gwen-schilling", Ct));
        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            gwen.Id, new InstructorUpdateViewModel(Medium: "Screen printing"), EntityTag.Format(gwen.RowVersion), Editor, Ct)));
        await host.SeedAsync(Ct);

        var all = await host.QueryAsync(context => context.Set<Instructor>().AsNoTracking().ToListAsync(Ct));
        Assert.Equal(InstructorDevelopmentSeeder.Roster.Count, all.Count);
        Assert.All(all, instructor => Assert.Equal(PublishStatus.Published, instructor.Status));
        Assert.Equal("Printmaking", gwen.Medium);
        Assert.Equal("Screen printing", all.Single(instructor => instructor.Slug == "gwen-schilling").Medium);
        Assert.Equal(InstructorDevelopmentSeeder.SeedActor, all.First(instructor => instructor.Slug == "diego-granath").UpdatedBy);
        Assert.Single(await host.AuditActionsAsync(), action => action == "instructor.seeded");

        // The one the spike links to the shop is linked to the artist the roster seed made; nobody else is.
        var candice = all.Single(instructor => instructor.Slug == "candice-johnson");
        Assert.NotNull(candice.ArtistId);
        Assert.All(all.Where(instructor => instructor != candice), instructor => Assert.Null(instructor.ArtistId));
    }

    [Fact]
    public async Task WebsiteAndInstagramAreKeptInOneSpellingAndAnEmptyStringClearsThem()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);

        var instructor = await CreateAsync(
            host, new InstructorCreateViewModel("Gwen Schilling", Website: " https://gwen.studio/ ", Instagram: " @Gwen.Prints "));
        var cleared = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            instructor.Id, new InstructorUpdateViewModel(Website: "", Instagram: "  "), instructor.ETag, Editor, Ct)));

        Assert.Equal(("https://gwen.studio/", "gwen.prints"), (instructor.Website, instructor.Instagram));
        Assert.Equal((null, null), (cleared.Website, cleared.Instagram));
    }

    [Fact]
    public async Task AnArtistLinkMustBeOnTheRosterAndNotArchivedAndComesBackDescribed()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var candice = await host.AddArtistAsync("Candice Johnson", PublishStatus.Draft);
        var retired = await host.AddArtistAsync("Retired Person", PublishStatus.Archived);

        var unknown = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new InstructorCreateViewModel("A", ArtistId: Guid.NewGuid()), Editor, Ct));
        var archived = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new InstructorCreateViewModel("B", ArtistId: retired), Editor, Ct));
        var linked = await CreateAsync(host, new InstructorCreateViewModel("Candice Johnson", ArtistId: candice));

        // The artist is archived after the link was made: sending the same link again is not a new one.
        await host.ExecuteAsync(async context =>
        {
            var artist = await context.Set<Arts6311.Domain.Modules.Artists.Data.Entities.Artist>().SingleAsync(row => row.Id == candice, Ct);
            artist.Status = PublishStatus.Archived;
            await context.SaveChangesAsync(Ct);
        });
        var resent = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            linked.Id, new InstructorUpdateViewModel(Medium: "Drawing", ArtistId: candice), linked.ETag, Editor, Ct)));
        var unlinked = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            linked.Id, new InstructorUpdateViewModel(RemoveArtist: true), resent.ETag, Editor, Ct)));

        Assert.Contains("artistId", unknown.Error!.FieldErrors.Keys);
        Assert.Contains("artistId", archived.Error!.FieldErrors.Keys);
        Assert.Equal(new InstructorArtistServiceModel(candice, "Candice Johnson", "candice-johnson", PublishStatus.Draft), linked.Artist);
        Assert.Equal(PublishStatus.Archived, resent.Artist!.Status);
        Assert.Null(unlinked.Artist);
        Assert.Null((await host.StoredAsync(linked.Id))!.ArtistId);
    }

    [Fact]
    public async Task TheSiteSeesOnlyPublishedInstructorsAndLinksAnArtistOnlyOnceTheirPageIsPublished()
    {
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var draftArtist = await host.AddArtistAsync("Candice Johnson", PublishStatus.Draft);
        var publishedArtist = await host.AddArtistAsync("Gwen Schilling");

        var candice = await CreateAsync(host, new InstructorCreateViewModel("Candice Johnson", ArtistId: draftArtist));
        var gwen = await CreateAsync(host, new InstructorCreateViewModel("Gwen Schilling", Instagram: "gwen", ArtistId: publishedArtist));
        await CreateAsync(host, new InstructorCreateViewModel("Still Drafting"));
        await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(candice.Id, candice.ETag, Editor, Ct)));
        await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(gwen.Id, gwen.ETag, Editor, Ct)));

        var list = await Ok(host.WithFacadeAsync(facade => facade.ListForSiteAsync(Ct)));
        var page = await Ok(host.WithFacadeAsync(facade => facade.GetForSiteAsync("gwen-schilling", Ct)));
        var draft = await host.WithFacadeAsync(facade => facade.GetForSiteAsync("still-drafting", Ct));

        Assert.Equal(["Candice Johnson", "Gwen Schilling"], list.Select(instructor => instructor.Name));
        Assert.Null(list[0].ArtistSlug);
        Assert.Equal(("gwen-schilling", "gwen"), (page.ArtistSlug, page.Instagram));
        Assert.Equal(ErrorCodes.NotFound, draft.Error!.Code);
    }

    [Fact]
    public async Task AnotherModuleLooksInstructorsUpByIdOrSlugWhateverTheirStatus()
    {
        // Classes' runs (ADR 0044): every instructor that exists comes back, archived ones included, and an id or
        // slug with no instructor is simply absent.
        await using var host = await InstructorsTestHost.CreateAsync(Ct);
        var jane = await CreateAsync(host, new InstructorCreateViewModel("Jane Doe"));
        var john = await CreateAsync(host, new InstructorCreateViewModel("John Doe"));
        await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(john.Id, john.ETag, InstructorsTestHost.AdminId, Ct)));

        var byId = await host.WithFacadeAsync(facade => facade.GetSummariesAsync([jane.Id, john.Id, Guid.NewGuid(), jane.Id], Ct));
        var bySlug = await host.WithFacadeAsync(facade => facade.FindBySlugsAsync(["john-doe", "nobody"], Ct));
        var none = await host.WithFacadeAsync(facade => facade.GetSummariesAsync([], Ct));

        Assert.Equal(["Jane Doe", "John Doe"], byId.Select(instructor => instructor.Name).Order(StringComparer.Ordinal));
        Assert.Equal(PublishStatus.Archived, byId.Single(instructor => instructor.Id == john.Id).Status);
        Assert.Equal(john.Id, Assert.Single(bySlug).Id);
        Assert.Empty(none);
    }

    private static async Task<List<RedirectSlugChanged>> StagedSlugChangesAsync(InstructorsTestHost host) =>
        [.. (await host.QueryAsync(context => context.OutboxMessages.AsNoTracking()
                .Where(message => message.Type == RedirectSlugChanged.MessageType).Select(message => message.PayloadJson).ToListAsync(Ct)))
            .Select(payload => RedirectSlugChanged.TryParse(payload)!)];

    private static async Task<InstructorServiceModel> CreateAsync(InstructorsTestHost host, InstructorCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Code + ": " + result.Error?.Message);
        return result.Value!;
    }

    private static Task SetAltTextAsync(InstructorsTestHost host, Guid assetId, string altText) =>
        host.ExecuteAsync(async context =>
        {
            var asset = await context.Set<MediaAsset>().SingleAsync(row => row.Id == assetId, Ct);
            asset.AltText = altText;
            await context.SaveChangesAsync(Ct);
        });

    private static async Task<OperationResult> DeleteImageAsync(InstructorsTestHost host, Guid assetId)
    {
        var version = await host.QueryAsync(context =>
            context.Set<MediaAsset>().AsNoTracking().Where(row => row.Id == assetId).Select(row => row.RowVersion).SingleAsync(Ct));

        using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediaFacade>()
            .DeleteAsync(assetId, EntityTag.Format(version), "admin-1", Ct);
    }
}
