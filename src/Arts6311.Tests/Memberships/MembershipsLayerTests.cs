using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Memberships.Business;
using Arts6311.Domain.Modules.Memberships.Data;
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.Validation;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;
using Arts6311.Tests.Infrastructure;
using Arts6311.Tests.Memberships.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Memberships;

/// <summary>
/// Each layer against fakes of the one below (backend.md "Testing"): the DataLayer's call order and failure
/// mapping, Business's rules and outcome mapping, and the facade's validation and preconditions.
/// </summary>
public sealed class MembershipsLayerTests
{
    private const string AnyTag = "\"0123456789abcdef0123456789abcdef\"";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- DataLayer ----

    [Fact]
    public async Task TheDataLayerStagesTheAuditRowBeforeItsOneSaveAndStampsTheWrite()
    {
        var (dataLayer, repository, _, log) = DataLayer();
        var plan = new FakeMembershipsDataLayer().Add();
        repository.Rows.Add(plan);
        var before = plan.RowVersion;

        var outcome = await dataLayer.UpdateAsync(plan, "editor-2", "Updated: perks.", Ct);

        Assert.Equal(MembershipWriteOutcome.Saved, outcome);
        Assert.Equal(["audit:membership-plan.updated", "save"], log.Calls);
        Assert.Equal(("editor-2", Now), (plan.UpdatedBy, plan.UpdatedAt));
        Assert.NotEqual(before, plan.RowVersion);
    }

    [Fact]
    public async Task EveryStatusChangeStagesRevalidationForTheOnePageBeforeTheOneSave()
    {
        var (dataLayer, repository, outbox, log) = DataLayer();
        var plan = new FakeMembershipsDataLayer().Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));
        repository.Rows.Add(plan);

        await dataLayer.TransitionAsync(plan, PublishCommand.Unpublish, PublishStatus.Published, "editor-1", Ct);

        Assert.Equal(["outbox:site.revalidate-requested", "audit:membership-plan.unpublished", "save"], log.Calls);

        var staged = SiteRevalidateRequested.TryParse(Assert.Single(outbox.Staged).Payload);
        Assert.Equal("membership-plan", staged!.EntityType);
        Assert.Equal(plan.Id, staged.EntityId);
        Assert.Equal(["/memberships"], staged.Paths);
    }

    [Fact]
    public async Task ASlugChangeStagesNoRedirectBecauseAPlanHasNoAddressOfItsOwn()
    {
        var (dataLayer, repository, outbox, _) = DataLayer();
        var plan = new FakeMembershipsDataLayer().Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));
        repository.Rows.Add(plan);
        plan.Slug = "grey-membership";

        await dataLayer.UpdateAsync(plan, "editor-1", "Updated: slug.", Ct);

        // No redirect: a plan has no address of its own (ADR 0040). It does owe a revalidation, because the
        // one page every tier shares is showing the name that just changed.
        var onlyMessage = Assert.Single(outbox.Staged);
        Assert.Equal(SiteRevalidateRequested.MessageType, onlyMessage.Type);
    }

    [Fact]
    public async Task AbandoningAScheduleRevalidatesAndNamesNobody()
    {
        var (dataLayer, repository, _, log) = DataLayer();
        var plan = new FakeMembershipsDataLayer().Add(PublishStatus.Scheduled);
        repository.Rows.Add(plan);

        await dataLayer.AbandonScheduleAsync(plan, PublishStatus.Scheduled, "No price.", Ct);

        Assert.Equal(
            ["outbox:site.revalidate-requested", "audit:membership-plan.schedule-abandoned", "save"], log.Calls);
        Assert.Equal(ScheduledPublishPolicy.Actor, plan.UpdatedBy);
    }

    [Fact]
    public async Task AFailedSaveIsMappedToTheClashItCanProveAndRethrownOtherwise()
    {
        var (dataLayer, repository, _, _) = DataLayer();
        var plan = new FakeMembershipsDataLayer().Add();

        repository.SaveFailure = new DbUpdateConcurrencyException("moved");
        var conflict = await dataLayer.UpdateAsync(plan, "editor-1", "Updated.", Ct);

        repository.SaveFailure = new DbUpdateException("unique");
        repository.SlugExists = true;
        var slugTaken = await dataLayer.UpdateAsync(plan, "editor-1", "Updated.", Ct);

        repository.SaveFailure = new DbUpdateException("something else");
        repository.SlugExists = false;

        Assert.Equal(MembershipWriteOutcome.Conflict, conflict);
        Assert.Equal(MembershipWriteOutcome.SlugTaken, slugTaken);
        await Assert.ThrowsAsync<DbUpdateException>(() => dataLayer.UpdateAsync(plan, "editor-1", "Updated.", Ct));
    }

    // ---- Business ----

    [Fact]
    public async Task BusinessMakesTheSlugFromTheNameAndPutsANewTierLast()
    {
        var (business, dataLayer) = Business();
        dataLayer.NextDisplayOrder = 4;

        var created = await business.CreateAsync(new MembershipPlanCreateViewModel("Blue Plus"), "editor-1", Ct);

        Assert.Equal("blue-plus", created.Value!.Slug);
        Assert.Equal(4, created.Value.DisplayOrder);
        Assert.Equal(MembershipCadence.Annual, created.Value.StartingCadence);
        Assert.Equal(PublishStatus.Draft, created.Value.Status);

        // The one address every tier shares comes back on the record, so the admin can link to it.
        Assert.Equal("/memberships", created.Value.SitePath);
    }

    [Fact]
    public async Task BusinessRefusesASlugAnotherPlanHolds()
    {
        var (business, dataLayer) = Business();
        dataLayer.SlugExists = true;

        var refused = await business.CreateAsync(new MembershipPlanCreateViewModel("Gray"), "editor-1", Ct);

        Assert.Equal(ContentErrorCodes.SlugTaken, refused.Error!.Code);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task PublishingNeedsAPriceAndAtLeastOnePerkAndSaysBothAtOnce()
    {
        var (business, dataLayer) = Business();
        var bare = dataLayer.Add(startingPriceCents: null, perks: []);

        var refused = await business.TransitionAsync(bare.Id, PublishCommand.Publish, Tag(bare.RowVersion), "editor-1", Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Equal(["startingPriceCents", "perks"], refused.Error.FieldErrors.Keys);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task TheStartingPriceAndCadenceFreezeOnceSquareHoldsThePlan()
    {
        var (business, dataLayer) = Business();
        var synced = dataLayer.Add(priceCents: 12_000, cadence: "ANNUAL");

        var price = await business.UpdateAsync(
            synced.Id, new MembershipPlanUpdateViewModel(StartingPriceCents: 20_000), Tag(synced.RowVersion), "editor-1", Ct);
        var cadence = await business.UpdateAsync(
            synced.Id,
            new MembershipPlanUpdateViewModel(StartingCadence: MembershipCadence.Monthly),
            Tag(synced.RowVersion),
            "editor-1",
            Ct);
        var name = await business.UpdateAsync(
            synced.Id, new MembershipPlanUpdateViewModel(Name: "Gray Plus"), Tag(synced.RowVersion), "editor-1", Ct);

        Assert.Equal(["startingPriceCents"], price.Error!.FieldErrors.Keys);
        Assert.Equal(["startingCadence"], cadence.Error!.FieldErrors.Keys);

        // The copy is still the CMS's: only the billing figures freeze (ADR 0005).
        Assert.True(name.Succeeded, name.Error?.Message);
        Assert.Equal(10_000, synced.StartingPriceCents);
    }

    [Fact]
    public async Task EditingThePerksRewritesThemInPlaceInTheOrderGiven()
    {
        var (business, dataLayer) = Business();
        var plan = dataLayer.Add(perks: ["5% off", "Party", "Tote"]);

        var reordered = await business.UpdateAsync(
            plan.Id,
            new MembershipPlanUpdateViewModel(Perks: ["Tote", "5% off"]),
            Tag(plan.RowVersion),
            "editor-1",
            Ct);

        Assert.True(reordered.Succeeded, reordered.Error?.Message);
        Assert.Equal(["Tote", "5% off"], reordered.Value!.Perks);
        Assert.Equal([0, 1], plan.Perks.Select(perk => perk.Position).Order());
        Assert.Contains("perks", dataLayer.LastSummary);
    }

    [Fact]
    public async Task APublishedTierKeepsItsPerks()
    {
        var (business, dataLayer) = Business();
        var live = dataLayer.Add(PublishStatus.Published, publishedAt: Now.AddDays(-1), perks: ["5% off"]);

        var refused = await business.UpdateAsync(
            live.Id, new MembershipPlanUpdateViewModel(Perks: []), Tag(live.RowVersion), "editor-1", Ct);

        Assert.Equal(["perks"], refused.Error!.FieldErrors.Keys);
        Assert.Single(live.Perks);
    }

    [Fact]
    public async Task APublishedTierKeepsAPriceToo()
    {
        // The page shows one on every card, and publishing refuses a tier without one, so clearing it while the
        // tier is public would leave a blank card.
        var (business, dataLayer) = Business();
        var live = dataLayer.Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));

        var refused = await business.UpdateAsync(
            live.Id, new MembershipPlanUpdateViewModel(RemoveStartingPrice: true), Tag(live.RowVersion), "editor-1", Ct);

        Assert.Equal(["startingPriceCents"], refused.Error!.FieldErrors.Keys);
        Assert.Equal(10_000, live.StartingPriceCents);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task ARefusedEditLeavesTheTrackedPlanExactlyAsItWasFound()
    {
        // The tracked entity lives on the request's own DbContext, so a refusal that had already written to it
        // would be waiting for any later save in the same request.
        var (business, dataLayer) = Business();
        var plan = dataLayer.Add(perks: ["5% off every class"]);
        dataLayer.SlugExists = true;

        var refused = await business.UpdateAsync(
            plan.Id,
            new MembershipPlanUpdateViewModel(
                Name: "Gray Plus",
                Slug: "taken-slug",
                Perks: ["10% off every class"],
                DiscountPercent: 10,
                Featured: true,
                DisplayOrder: 4,
                StartingPriceCents: 20_000),
            Tag(plan.RowVersion),
            "editor-1",
            Ct);

        Assert.Equal(ContentErrorCodes.SlugTaken, refused.Error!.Code);
        Assert.Equal(("Gray", "gray-membership", 5), (plan.Name, plan.Slug, plan.DiscountPercent));
        Assert.Equal((false, 1, 10_000L), (plan.Featured, plan.DisplayOrder, plan.StartingPriceCents));
        Assert.Equal(["5% off every class"], plan.Perks.Select(perk => perk.Text));
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task AnEditThatChangesNothingSavesNothing()
    {
        var (business, dataLayer) = Business();
        var plan = dataLayer.Add(perks: ["5% off every class"]);

        var unchanged = await business.UpdateAsync(
            plan.Id,
            new MembershipPlanUpdateViewModel(Name: "Gray", Perks: ["5% off every class"], Featured: false),
            Tag(plan.RowVersion),
            "editor-1",
            Ct);

        Assert.True(unchanged.Succeeded, unchanged.Error?.Message);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task FeaturingASecondTierIsAllowed()
    {
        // A plain flag (ADR 0040): how many tiers are highlighted is the site's business, not an invariant here.
        var (business, dataLayer) = Business();
        var first = dataLayer.Add();
        first.Featured = true;
        var second = dataLayer.Add();

        var featured = await business.UpdateAsync(
            second.Id, new MembershipPlanUpdateViewModel(Featured: true), Tag(second.RowVersion), "editor-1", Ct);

        Assert.True(featured.Succeeded, featured.Error?.Message);
        Assert.True(first.Featured);
        Assert.True(second.Featured);
    }

    [Fact]
    public async Task AnIllegalTransitionAndAStaleVersionAreRefused()
    {
        var (business, dataLayer) = Business();
        var live = dataLayer.Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));

        var again = await business.TransitionAsync(live.Id, PublishCommand.Publish, Tag(live.RowVersion), "editor-1", Ct);
        var stale = await business.TransitionAsync(live.Id, PublishCommand.Unpublish, AnyTag, "editor-1", Ct);
        var gone = await business.TransitionAsync(Guid.NewGuid(), PublishCommand.Unpublish, AnyTag, "editor-1", Ct);

        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, again.Error!.Code);
        Assert.Equal(ErrorCodes.ConcurrencyConflict, stale.Error!.Code);
        Assert.Equal(ErrorCodes.NotFound, gone.Error!.Code);
        Assert.Empty(dataLayer.Writes);
    }

    [Fact]
    public async Task SchedulingRefusesATimeAlreadyPastAndChecksWhatPublishingNeeds()
    {
        var (business, dataLayer) = Business();
        var ready = dataLayer.Add();
        var bare = dataLayer.Add(startingPriceCents: null, perks: []);

        var past = await business.ScheduleAsync(ready.Id, Now.AddMinutes(-1), Tag(ready.RowVersion), "editor-1", Ct);
        var unready = await business.ScheduleAsync(bare.Id, Now.AddDays(1), Tag(bare.RowVersion), "editor-1", Ct);
        var scheduled = await business.ScheduleAsync(ready.Id, Now.AddDays(1), Tag(ready.RowVersion), "editor-1", Ct);

        Assert.Equal(["scheduledFor"], past.Error!.FieldErrors.Keys);
        Assert.Equal(["startingPriceCents", "perks"], unready.Error!.FieldErrors.Keys);
        Assert.True(scheduled.Succeeded, scheduled.Error?.Message);
        Assert.Equal(PublishStatus.Scheduled, ready.Status);
        Assert.Equal(Now.AddDays(1), ready.ScheduledFor);
    }

    [Fact]
    public async Task AScheduledPublishGoesBackToDraftWhenWhatPublishingNeedsIsGone()
    {
        var (business, dataLayer) = Business();
        var due = dataLayer.Add(PublishStatus.Scheduled, startingPriceCents: null, perks: []);
        due.ScheduledFor = Now.AddMinutes(-1);

        var outcome = await business.PublishScheduledAsync(due.Id, Now, Ct);

        Assert.Equal(ScheduledPublishOutcome.Unscheduled, outcome);
        Assert.Equal(PublishStatus.Draft, due.Status);
        Assert.Null(due.ScheduledFor);
        Assert.Contains("ready to publish", dataLayer.LastAbandon!.Value.Reason);
    }

    [Fact]
    public async Task AScheduledPublishSkipsAPlanThatMovedOnAndLosesOneAnotherWorkerTook()
    {
        var (business, dataLayer) = Business();
        var moved = dataLayer.Add(PublishStatus.Published, publishedAt: Now.AddDays(-1));
        var due = dataLayer.Add(PublishStatus.Scheduled);
        due.ScheduledFor = Now.AddMinutes(-1);

        var gone = await business.PublishScheduledAsync(Guid.NewGuid(), Now, Ct);
        var published = await business.PublishScheduledAsync(moved.Id, Now, Ct);

        dataLayer.Outcome = MembershipWriteOutcome.Conflict;
        var lost = await business.PublishScheduledAsync(due.Id, Now, Ct);

        Assert.Equal(ScheduledPublishOutcome.Skipped, gone);
        Assert.Equal(ScheduledPublishOutcome.Skipped, published);
        Assert.Equal(ScheduledPublishOutcome.Lost, lost);
    }

    [Fact]
    public async Task ABadCursorIsRefusedRatherThanReadAsThePageAfterNothing()
    {
        var (business, _) = Business();

        var refused = await business.ListAsync(new MembershipPlanListViewModel(Cursor: "not-a-cursor"), Ct);

        Assert.Equal(ErrorCodes.CursorInvalid, refused.Error!.Code);
    }

    [Fact]
    public async Task SeedingSkipsATierWhoseSlugIsAlreadyInUse()
    {
        var (business, dataLayer) = Business();
        dataLayer.Add();

        var added = await business.SeedAsync(MembershipDevelopmentSeeder.Tiers, "system:development-seed", Ct);

        Assert.Equal(2, added);
        Assert.Equal(["black-membership", "blue-membership"], dataLayer.Seeded.Select(plan => plan.Slug));
        Assert.All(dataLayer.Seeded, plan => Assert.Equal(PublishStatus.Published, plan.Status));
    }

    // ---- Facade ----

    [Fact]
    public async Task TheFacadeValidatesShapeBeforeBusinessSeesAnything()
    {
        var (facade, business) = Facade();

        var refused = await facade.CreateAsync(new MembershipPlanCreateViewModel(" "), "editor-1", Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task EveryWriteNeedsAnIfMatchBeforeAnythingIsRead()
    {
        var (facade, business) = Facade();
        var id = RecordingMembershipsBusiness.Plan.Id;

        var update = await facade.UpdateAsync(id, new MembershipPlanUpdateViewModel(Name: "Gray"), null, "editor-1", Ct);
        var publish = await facade.PublishAsync(id, null, "editor-1", Ct);
        var archive = await facade.ArchiveAsync(id, null, "editor-1", Ct);
        var schedule = await facade.ScheduleAsync(id, new ContentScheduleViewModel(Now.AddDays(1)), null, "editor-1", Ct);
        var unschedule = await facade.UnscheduleAsync(id, null, "editor-1", Ct);

        Assert.All(
            [update, publish, archive, schedule, unschedule],
            result => Assert.Equal(ErrorCodes.PreconditionRequired, result.Error!.Code));
        Assert.Empty(business.Calls);
    }

    [Fact]
    public async Task TheStatusCommandsReachBusinessAsThemselves()
    {
        var (facade, business) = Facade();
        var id = RecordingMembershipsBusiness.Plan.Id;
        var when = Now.AddDays(1);

        await facade.PublishAsync(id, AnyTag, "editor-1", Ct);
        await facade.UnpublishAsync(id, AnyTag, "editor-1", Ct);
        await facade.ArchiveAsync(id, AnyTag, "editor-1", Ct);
        await facade.ScheduleAsync(id, new ContentScheduleViewModel(when), AnyTag, "editor-1", Ct);
        await facade.UnscheduleAsync(id, AnyTag, "editor-1", Ct);

        Assert.Equal(
            ["transition:Publish", "transition:Unpublish", "transition:Archive", "schedule", "unschedule"],
            business.Calls);
        Assert.Equal(when, business.LastScheduledFor);
    }

    [Fact]
    public async Task AScheduleWithoutATimeInTheFutureNeverReachesBusiness()
    {
        var (facade, business) = Facade();

        var refused = await facade.ScheduleAsync(
            RecordingMembershipsBusiness.Plan.Id,
            new ContentScheduleViewModel(default),
            AnyTag,
            "editor-1",
            Ct);

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Empty(business.Calls);
    }

    private static string Tag(Guid rowVersion) => $"\"{rowVersion:N}\"";

    private static (MembershipsDataLayer, FakeMembershipsRepository, RecordingOutboxWriter, CallLog) DataLayer()
    {
        var log = new CallLog();
        var repository = new FakeMembershipsRepository(log);
        var outbox = new RecordingOutboxWriter(log);
        return (
            new MembershipsDataLayer(
                repository, new RecordingAuditWriter(log), outbox, new FixedCorrelation(), new FixedClock(Now)),
            repository,
            outbox,
            log);
    }

    private static (MembershipsBusiness, FakeMembershipsDataLayer) Business()
    {
        var dataLayer = new FakeMembershipsDataLayer();
        return (new MembershipsBusiness(dataLayer, new FixedClock(Now)), dataLayer);
    }

    private static (MembershipsFacade, RecordingMembershipsBusiness) Facade()
    {
        var business = new RecordingMembershipsBusiness();
        var facade = new MembershipsFacade(
            business,
            new MembershipPlanCreateValidator(),
            new MembershipPlanUpdateValidator(),
            new MembershipPlanListValidator(),
            new ContentScheduleValidator(),
            TestAppCache.Create());
        return (facade, business);
    }
}
