using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;
using Arts6311.Tests.Media.Infrastructure;
using Arts6311.Tests.Memberships.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Memberships;

/// <summary>
/// The module end to end through its facade, on a real (SQLite) database: the rules, the audit trail, the atomic
/// save, and what Square owns and the CMS may not write.
/// </summary>
public sealed class MembershipsModuleTests
{
    private const string Editor = MembershipsTestHost.EditorId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreatedTierIsADraftWithItsPerksInOrderAndAnAuditRow()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);

        var plan = await CreateAsync(host, new MembershipPlanCreateViewModel(
            "  Gray ",
            Perks: ["5% off every class & workshop", "Annual membership party"],
            DiscountPercent: 5,
            StartingPriceCents: 10_000));

        Assert.Equal(("Gray", "gray", PublishStatus.Draft), (plan.Name, plan.Slug, plan.Status));
        Assert.Equal(["5% off every class & workshop", "Annual membership party"], plan.Perks);
        Assert.Equal((5, false, 1), (plan.DiscountPercent, plan.Featured, plan.DisplayOrder));
        Assert.Equal((10_000L, MembershipCadence.Annual, "USD"), (plan.StartingPriceCents, plan.StartingCadence, plan.Currency));
        Assert.True(plan.StartingPriceEditable);
        Assert.Null(plan.PriceCents);
        Assert.Null(plan.Cadence);
        Assert.False(plan.SquareHasRecord);

        // Every tier shares the one page (ADR 0040).
        Assert.Equal("/memberships", plan.SitePath);
        Assert.Equal(["membership-plan.created"], await host.AuditActionsAsync());

        var stored = (await host.StoredAsync(plan.Id))!;
        Assert.Equal([0, 1], stored.Perks.Select(perk => perk.Position).Order());
    }

    [Fact]
    public async Task ATakenSlugIsRefusedOnTheSlugField()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new MembershipPlanCreateViewModel("Gray"));

        var second = await host.WithFacadeAsync(facade => facade.CreateAsync(
            new MembershipPlanCreateViewModel("GRAY"), Editor, Ct));

        Assert.Equal(ContentErrorCodes.SlugTaken, second.Error!.Code);
        Assert.Contains("slug", second.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task EachNewTierGoesAfterTheLastOne()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);

        var gray = await CreateAsync(host, new MembershipPlanCreateViewModel("Gray"));
        var black = await CreateAsync(host, new MembershipPlanCreateViewModel("Black"));
        var chosen = await CreateAsync(host, new MembershipPlanCreateViewModel("Blue", DisplayOrder: 9));

        Assert.Equal([1, 2, 9], new[] { gray, black, chosen }.Select(plan => plan.DisplayOrder));
    }

    [Fact]
    public async Task PublishingNeedsAPriceAndAPerkAndThenRevalidatesThePage()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var bare = await CreateAsync(host, new MembershipPlanCreateViewModel("Gray"));

        var refused = await host.WithFacadeAsync(facade => facade.PublishAsync(bare.Id, bare.ETag, Editor, Ct));

        var ready = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            bare.Id,
            new MembershipPlanUpdateViewModel(Perks: ["5% off every class"], StartingPriceCents: 10_000),
            bare.ETag,
            Editor,
            Ct)));
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(ready.Id, ready.ETag, Editor, Ct)));

        Assert.Equal(ErrorCodes.ValidationFailed, refused.Error!.Code);
        Assert.Equal(["startingPriceCents", "perks"], refused.Error.FieldErrors.Keys);
        Assert.Equal(PublishStatus.Published, published.Status);
        Assert.Equal(host.Time.GetUtcNow(), published.PublishedAt);

        var message = Assert.Single(await host.OutboxAsync());
        Assert.Equal(SiteRevalidateRequested.MessageType, message.Type);
        Assert.Equal(["/memberships"], SiteRevalidateRequested.TryParse(message.PayloadJson)!.Paths);
        Assert.Equal(
            ["membership-plan.created", "membership-plan.updated", "membership-plan.published"],
            await host.AuditActionsAsync());
    }

    [Fact]
    public async Task AStaleIfMatchIsA409AndAMissingOneIsRefusedOutright()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var plan = await ReadyAsync(host);
        var stale = plan.ETag;
        await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            plan.Id, new MembershipPlanUpdateViewModel(Name: "Gray Plus"), stale, Editor, Ct)));

        var conflict = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            plan.Id, new MembershipPlanUpdateViewModel(Name: "Gray Again"), stale, Editor, Ct));
        var missing = await host.WithFacadeAsync(facade => facade.PublishAsync(plan.Id, null, Editor, Ct));

        Assert.Equal(ErrorCodes.ConcurrencyConflict, conflict.Error!.Code);
        Assert.Equal(ErrorCodes.PreconditionRequired, missing.Error!.Code);
        Assert.Equal("Gray Plus", (await host.StoredAsync(plan.Id))!.Name);
    }

    [Fact]
    public async Task EditingThePerksReplacesThemWithoutLeavingOldRowsBehind()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var plan = await CreateAsync(host, new MembershipPlanCreateViewModel(
            "Blue", Perks: ["15% off", "Party", "Tote", "T-shirt"], StartingPriceCents: 50_000));

        var trimmed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            plan.Id, new MembershipPlanUpdateViewModel(Perks: ["Party", "15% off"]), plan.ETag, Editor, Ct)));

        Assert.Equal(["Party", "15% off"], trimmed.Perks);

        var stored = (await host.StoredAsync(plan.Id))!;
        Assert.Equal(
            ["Party", "15% off"],
            stored.Perks.OrderBy(perk => perk.Position).Select(perk => perk.Text));
        Assert.Equal(2, await host.QueryAsync(context => context.Set<MembershipPlanPerk>().CountAsync(Ct)));
    }

    [Fact]
    public async Task ASlugChangeOnAPublishedTierOwesNoRedirect()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var plan = await ReadyAsync(host);
        var published = await Ok(host.WithFacadeAsync(facade => facade.PublishAsync(plan.Id, plan.ETag, Editor, Ct)));

        var renamed = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            plan.Id, new MembershipPlanUpdateViewModel(Slug: "grey-membership"), published.ETag, Editor, Ct)));

        Assert.Equal("grey-membership", renamed.Slug);

        // No address was ever its own, so nothing is redirected (ADR 0040). Two revalidations, though: one for
        // the publish and one for the rename, because /memberships is live and showing this tier.
        var messages = await host.OutboxAsync();
        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.Equal(SiteRevalidateRequested.MessageType, message.Type));
    }

    [Fact]
    public async Task TheCmsNeverWritesWhatSquareHoldsAndItsOwnFiguresFreezeOnceSquareDoes()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var plan = await ReadyAsync(host);

        // As the 5.x inbound pull will: the projections are written by nothing in this module.
        await host.SquareReportedAsync(plan.Id, 12_000, "ANNUAL");
        var read = await Ok(host.WithFacadeAsync(facade => facade.GetAsync(plan.Id, Ct)));

        var refused = await host.WithFacadeAsync(facade => facade.UpdateAsync(
            plan.Id, new MembershipPlanUpdateViewModel(StartingPriceCents: 20_000), read.ETag, Editor, Ct));
        var copy = await Ok(host.WithFacadeAsync(facade => facade.UpdateAsync(
            plan.Id, new MembershipPlanUpdateViewModel(Perks: ["10% off every class"]), read.ETag, Editor, Ct)));

        Assert.Equal((12_000L, "ANNUAL", true), (read.PriceCents, read.Cadence, read.SquareHasRecord));
        Assert.False(read.StartingPriceEditable);
        Assert.Equal(["startingPriceCents"], refused.Error!.FieldErrors.Keys);

        // The copy is still the CMS's, and the projections it read back are untouched by the edit.
        Assert.Equal(["10% off every class"], copy.Perks);
        Assert.Equal((12_000L, "ANNUAL"), (copy.PriceCents, copy.Cadence));
        Assert.Equal(10_000, (await host.StoredAsync(plan.Id))!.StartingPriceCents);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheTierNorItsPerks()
    {
        var failAudit = new FailInsertInto("AuditLogs");
        await using var host = await MembershipsTestHost.CreateAsync(Ct, failAudit);

        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.CreateAsync(
            new MembershipPlanCreateViewModel("Gray", Perks: ["5% off"]), Editor, Ct)));

        Assert.Empty(await host.QueryAsync(context => context.Set<MembershipPlan>().AsNoTracking().ToListAsync(Ct)));
        Assert.Empty(await host.QueryAsync(context => context.Set<MembershipPlanPerk>().AsNoTracking().ToListAsync(Ct)));
    }

    [Fact]
    public async Task AFailedStatusChangeLeavesNeitherTheStatusNorItsRevalidation()
    {
        var failAudit = new FailInsertInto("AuditLogs") { Armed = false };
        await using var host = await MembershipsTestHost.CreateAsync(Ct, failAudit);
        var plan = await ReadyAsync(host);

        failAudit.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => host.WithFacadeAsync(facade => facade.PublishAsync(
            plan.Id, plan.ETag, Editor, Ct)));

        Assert.Equal(PublishStatus.Draft, (await host.StoredAsync(plan.Id))!.Status);
        Assert.Empty(await host.OutboxAsync());
    }

    [Fact]
    public async Task AScheduledTierIsPublishedByTheWorkerOnceAndThenSkipped()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var plan = await ReadyAsync(host);
        var when = host.Time.GetUtcNow().AddHours(2);

        var scheduled = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            plan.Id, new ContentScheduleViewModel(when), plan.ETag, Editor, Ct)));
        var due = await host.WithFacadeAsync(facade => facade.ListDueAsync(when, 10, Ct));
        var first = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(plan.Id, when, Ct));
        var second = await host.WithFacadeAsync(facade => facade.PublishScheduledAsync(plan.Id, when, Ct));

        Assert.Equal((PublishStatus.Scheduled, when), (scheduled.Status, scheduled.ScheduledFor));
        Assert.Equal([plan.Id], due);
        Assert.Equal(ScheduledPublishOutcome.Published, first);
        Assert.Equal(ScheduledPublishOutcome.Skipped, second);

        var stored = (await host.StoredAsync(plan.Id))!;
        Assert.Equal((PublishStatus.Published, (DateTimeOffset?)null), (stored.Status, stored.ScheduledFor));
        Assert.Equal(ScheduledPublishPolicy.Actor, stored.UpdatedBy);
    }

    [Fact]
    public async Task AScheduledTimeIsAnsweredAsUtcWhateverOffsetWasSent()
    {
        // api-contract.md: UTC with an explicit "+00:00". A caller in Chicago sends -05:00; the same instant comes
        // back spelt the one way the contract allows.
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var plan = await ReadyAsync(host);

        var scheduled = await Ok(host.WithFacadeAsync(facade => facade.ScheduleAsync(
            plan.Id,
            new ContentScheduleViewModel(new DateTimeOffset(2026, 12, 1, 4, 0, 0, TimeSpan.FromHours(-5))),
            plan.ETag,
            Editor,
            Ct)));

        Assert.Equal(TimeSpan.Zero, scheduled.ScheduledFor!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), scheduled.ScheduledFor);
        Assert.Equal(TimeSpan.Zero, (await host.StoredAsync(plan.Id))!.ScheduledFor!.Value.Offset);
    }

    [Fact]
    public async Task AnArchivedTierTakesNoFurtherCommand()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        var plan = await ReadyAsync(host);

        var archived = await Ok(host.WithFacadeAsync(facade => facade.ArchiveAsync(plan.Id, plan.ETag, Editor, Ct)));
        var published = await host.WithFacadeAsync(facade => facade.PublishAsync(plan.Id, archived.ETag, Editor, Ct));

        Assert.Equal(PublishStatus.Archived, archived.Status);
        Assert.Equal(ContentErrorCodes.StatusTransitionInvalid, published.Error!.Code);
    }

    [Fact]
    public async Task TheListComesBackInDisplayOrderWithWhatStaffScanFor()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct);
        await CreateAsync(host, new MembershipPlanCreateViewModel("Gray", Perks: ["5% off"], DiscountPercent: 5, StartingPriceCents: 10_000));
        await CreateAsync(host, new MembershipPlanCreateViewModel("Black", Perks: ["10% off"], DiscountPercent: 10, Featured: true, StartingPriceCents: 30_000));

        var page = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new MembershipPlanListViewModel(), Ct)));

        Assert.Equal(["Gray", "Black"], page.Items.Select(row => row.Name));
        Assert.Equal([false, true], page.Items.Select(row => row.Featured));
        Assert.Equal([5, 10], page.Items.Select(row => row.DiscountPercent));
        Assert.All(page.Items, row => Assert.Equal("USD", row.Currency));
        Assert.All(page.Items, row => Assert.Equal(1, row.PerkCount));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task TheDevelopmentSeedAddsTheSpikesThreeTiersOnce()
    {
        await using var host = await MembershipsTestHost.CreateAsync(Ct, withSeeder: true);

        await host.SeedAsync(Ct);
        await host.SeedAsync(Ct);

        var page = await Ok(host.WithFacadeAsync(facade => facade.ListAsync(new MembershipPlanListViewModel(), Ct)));

        Assert.Equal(["Gray", "Black", "Blue"], page.Items.Select(row => row.Name));
        Assert.Equal([10_000L, 30_000L, 50_000L], page.Items.Select(row => row.StartingPriceCents));
        Assert.Equal([5, 10, 15], page.Items.Select(row => row.DiscountPercent));
        Assert.Equal([false, true, false], page.Items.Select(row => row.Featured));
        Assert.All(page.Items, row => Assert.Equal(PublishStatus.Published, row.Status));

        // No projection is seeded: what Square bills is Square's to report (ADR 0005).
        Assert.All(page.Items, row => Assert.Null(row.PriceCents));
        Assert.All(page.Items, row => Assert.Null(row.Cadence));

        var seeded = await host.QueryAsync(context => context.Set<MembershipPlan>().AsNoTracking()
            .CountAsync(plan => plan.UpdatedBy == MembershipDevelopmentSeeder.SeedActor, Ct));
        Assert.Equal(3, seeded);
    }

    private static async Task<MembershipPlanServiceModel> CreateAsync(
        MembershipsTestHost host, MembershipPlanCreateViewModel create) =>
        await Ok(host.WithFacadeAsync(facade => facade.CreateAsync(create, Editor, Ct)));

    /// <summary>A tier with everything publishing needs.</summary>
    private static Task<MembershipPlanServiceModel> ReadyAsync(MembershipsTestHost host) =>
        CreateAsync(host, new MembershipPlanCreateViewModel(
            "Gray",
            Slug: "gray-membership",
            Perks: ["5% off every class & workshop"],
            DiscountPercent: 5,
            StartingPriceCents: 10_000));

    private static async Task<T> Ok<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        Assert.True(result.Succeeded, result.Error?.Message);
        return result.Value!;
    }
}
