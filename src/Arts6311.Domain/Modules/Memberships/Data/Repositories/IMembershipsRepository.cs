using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Memberships.Data.Repositories;

internal interface IMembershipsRepository
{
    void Add(MembershipPlan plan);

    /// <summary>One plan with its perks, tracked, for a read, an edit or a status command.</summary>
    Task<MembershipPlan?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another plan than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the development seed, which adds only the tiers that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Where a new plan goes: after every existing one.</summary>
    Task<int> NextDisplayOrderAsync(CancellationToken cancellationToken);

    /// <summary>One page of the list in (DisplayOrder, Id) order, projected straight to list rows.</summary>
    Task<List<MembershipPlanSummaryServiceModel>> ListAsync(
        MembershipPlanListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        CancellationToken cancellationToken);

    /// <summary>
    /// The ids of plans Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036). Ids only, because the pass reads
    /// each plan again before it acts on it.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Every published plan with its perks, untracked, in display order (ADR 0042).</summary>
    Task<List<MembershipPlan>> ListPublishedAsync(CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
