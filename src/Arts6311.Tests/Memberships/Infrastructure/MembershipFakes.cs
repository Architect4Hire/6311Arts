using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Memberships.Business;
using Arts6311.Domain.Modules.Memberships.Data;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Data.Repositories;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;
using Arts6311.Tests.Artists.Infrastructure;

namespace Arts6311.Tests.Memberships.Infrastructure;

/// <summary>A repository over a list, whose save can be told to fail the way EF fails.</summary>
internal sealed class FakeMembershipsRepository(CallLog log) : IMembershipsRepository
{
    public Task<MembershipPlan?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<MembershipPlanDeletedRow>> ListDeletedAsync(
        MembershipPlanListPosition? after, int take, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<global::Arts6311.Domain.Modules.Memberships.Data.Entities.MembershipPlan>> ListPublishedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public List<MembershipPlan> Rows { get; } = [];

    /// <summary>Thrown by the next save, then cleared.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>What <see cref="SlugExistsAsync"/> answers when set; otherwise it looks at <see cref="Rows"/>.</summary>
    public bool? SlugExists { get; set; }

    public int NextDisplayOrder { get; set; } = 1;

    public void Add(MembershipPlan plan)
    {
        log.Add("add");
        Rows.Add(plan);
    }

    public Task<MembershipPlan?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken)
    {
        log.Add("slug-exists");
        return Task.FromResult(SlugExists ?? Rows.Any(row => row.Slug == slug && row.Id != exceptId));
    }

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<int> NextDisplayOrderAsync(CancellationToken cancellationToken) => Task.FromResult(NextDisplayOrder);

    public Task<List<MembershipPlanSummaryServiceModel>> ListAsync(
        MembershipPlanListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        CancellationToken cancellationToken) =>
        Task.FromResult(new List<MembershipPlanSummaryServiceModel>());

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        log.Add("list-due");
        return Task.FromResult<IReadOnlyList<Guid>>(
        [
            .. Rows
                .Where(row => row.Status == PublishStatus.Scheduled && row.ScheduledFor is { } when && when <= asOf)
                .OrderBy(row => row.ScheduledFor)
                .Take(take)
                .Select(row => row.Id),
        ]);
    }

    public void DiscardChanges() => log.Add("discard");

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        log.Add("save");
        if (SaveFailure is { } failure)
        {
            SaveFailure = null;
            throw failure;
        }

        return Task.CompletedTask;
    }
}

/// <summary>A DataLayer over a dictionary that records what it was asked to write and answers a chosen outcome.</summary>
internal sealed class FakeMembershipsDataLayer : IMembershipsDataLayer
{
    public Task<MembershipPlan?> FindDeletedAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<MembershipPlanDeletedRow>> ListDeletedAsync(
        MembershipPlanListPosition? after, int take, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<MembershipWriteOutcome> DeleteAsync(
        MembershipPlan entity, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<MembershipWriteOutcome> RestoreAsync(
        MembershipPlan entity, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<List<global::Arts6311.Domain.Modules.Memberships.Data.Entities.MembershipPlan>> ListPublishedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<Guid, MembershipPlan> Rows { get; } = [];

    public MembershipWriteOutcome Outcome { get; set; } = MembershipWriteOutcome.Saved;

    public bool SlugExists { get; set; }

    public int NextDisplayOrder { get; set; } = 4;

    public List<string> Writes { get; } = [];

    public string? LastSummary { get; private set; }

    public (PublishCommand Command, PublishStatus From)? LastTransition { get; private set; }

    public (PublishStatus From, string Reason)? LastAbandon { get; private set; }

    public List<MembershipPlan> Seeded { get; } = [];

    public Task<MembershipPlan?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(SlugExists);

    public Task<int> NextDisplayOrderAsync(CancellationToken cancellationToken) => Task.FromResult(NextDisplayOrder);

    public Task<List<MembershipPlanSummaryServiceModel>> ListAsync(
        MembershipPlanListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        CancellationToken cancellationToken) =>
        Task.FromResult(new List<MembershipPlanSummaryServiceModel>());

    public Task<MembershipWriteOutcome> CreateAsync(
        MembershipPlan plan, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        Writes.Add("create");
        LastSummary = summary;
        if (Outcome == MembershipWriteOutcome.Saved)
        {
            Rows[plan.Id] = plan;
        }

        return Task.FromResult(Outcome);
    }

    public Task<MembershipWriteOutcome> UpdateAsync(
        MembershipPlan plan, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        Writes.Add("update");
        LastSummary = summary;
        return Task.FromResult(Outcome);
    }

    public Task<MembershipWriteOutcome> TransitionAsync(
        MembershipPlan plan,
        PublishCommand command,
        PublishStatus from,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Writes.Add($"transition:{command}");
        LastTransition = (command, from);
        return Task.FromResult(Outcome);
    }

    public Task<MembershipWriteOutcome> AbandonScheduleAsync(
        MembershipPlan plan, PublishStatus from, string reason, CancellationToken cancellationToken)
    {
        Writes.Add("abandon");
        LastAbandon = (from, reason);
        return Task.FromResult(Outcome);
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([.. Rows.Values
            .Where(row => row.Status == PublishStatus.Scheduled && row.ScheduledFor is { } when && when <= asOf)
            .Select(row => row.Id)]);

    public Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Values.Select(row => row.Slug).ToHashSet(StringComparer.Ordinal));

    public Task<int> AddSeedAsync(
        IReadOnlyList<MembershipPlan> plans, string actor, CancellationToken cancellationToken)
    {
        Writes.Add("seed");
        Seeded.AddRange(plans);
        return Task.FromResult(plans.Count);
    }

    /// <summary>A stored plan, as a staff write would have left it.</summary>
    public MembershipPlan Add(
        PublishStatus status = PublishStatus.Draft,
        DateTimeOffset? publishedAt = null,
        long? startingPriceCents = 10_000,
        string[]? perks = null,
        long? priceCents = null,
        string? cadence = null)
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Gray",
            Slug = "gray-membership",
            DiscountPercent = 5,
            DisplayOrder = 1,
            StartingPriceCents = startingPriceCents,
            StartingCadence = MembershipCadence.Annual,
            PriceCents = priceCents,
            Cadence = cadence,
            Status = status,
            PublishedAt = publishedAt,
            UpdatedBy = "editor-1",
            RowVersion = Guid.NewGuid(),
        };

        foreach (var (perk, position) in (perks ?? ["5% off every class"]).Select((perk, position) => (perk, position)))
        {
            plan.Perks.Add(new MembershipPlanPerk { MembershipPlanId = plan.Id, Position = position, Text = perk });
        }

        Rows[plan.Id] = plan;
        return plan;
    }
}

/// <summary>Business that records what the facade asked and answers what the test set.</summary>
internal sealed class RecordingMembershipsBusiness : IMembershipsBusiness
{
    public Task<OperationResult<CursorPage<MembershipPlanDeletedServiceModel>>> ListDeletedAsync(
        MembershipPlanListViewModel request, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult> DeleteAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OperationResult<MembershipPlanServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Modules.Memberships.Managers.ServiceModels.SiteMembershipPlanServiceModel>> ListForSiteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<global::Arts6311.Domain.Managers.Results.OperationResult<global::Arts6311.Domain.Managers.Publishing.PreviewRecord<global::Arts6311.Domain.Modules.Memberships.Managers.ServiceModels.SiteMembershipPlanServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<global::Arts6311.Domain.Managers.Publishing.SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public static readonly MembershipPlanServiceModel Plan = new(
        Guid.Parse("66666666-6666-6666-6666-666666666666"),
        "Gray",
        "gray-membership",
        ["5% off every class & workshop"],
        DiscountPercent: 5,
        Featured: false,
        DisplayOrder: 1,
        StartingPriceCents: 10_000,
        MembershipCadence.Annual,
        CatalogPolicy.Currency,
        StartingPriceEditable: true,
        PriceCents: null,
        Cadence: null,
        SquareHasRecord: false,
        PublishStatus.Draft,
        null,
        null,
        MembershipPolicy.SitePath,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        "editor-1",
        EntityTag.Format(Guid.Parse("0123456789abcdef0123456789abcdef")));

    public List<string> Calls { get; } = [];

    public MembershipPlanServiceModel Current { get; set; } = Plan;

    public DateTimeOffset? LastScheduledFor { get; private set; }

    public IReadOnlyList<Guid> Due { get; set; } = [];

    public ScheduledPublishOutcome ScheduledOutcome { get; set; } = ScheduledPublishOutcome.Published;

    public List<MembershipPlanSummaryServiceModel> Page { get; } = [];

    public Task<OperationResult<CursorPage<MembershipPlanSummaryServiceModel>>> ListAsync(
        MembershipPlanListViewModel request, CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult(OperationResult.Success(
            new CursorPage<MembershipPlanSummaryServiceModel>(Page, null)));
    }

    public Task<OperationResult<MembershipPlanServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add("get");
        return Task.FromResult(id == Current.Id
            ? OperationResult.Success(Current)
            : OperationResult.Failure<MembershipPlanServiceModel>(OperationError.Of(ErrorCodes.NotFound, "No.")));
    }

    public Task<OperationResult<MembershipPlanServiceModel>> CreateAsync(
        MembershipPlanCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("create");
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<MembershipPlanServiceModel>> UpdateAsync(
        Guid id,
        MembershipPlanUpdateViewModel update,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("update");
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<MembershipPlanServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add($"transition:{command}");
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<MembershipPlanServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        Calls.Add("schedule");
        LastScheduledFor = scheduledFor;
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<OperationResult<MembershipPlanServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        Calls.Add("unschedule");
        return Task.FromResult(OperationResult.Success(Current));
    }

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
    {
        Calls.Add("list-due");
        return Task.FromResult(Due);
    }

    public Task<ScheduledPublishOutcome> PublishScheduledAsync(
        Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        Calls.Add("publish-scheduled");
        return Task.FromResult(ScheduledOutcome);
    }

    public Task<int> SeedAsync(
        IReadOnlyList<MembershipPlanSeed> seeds, string actor, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
