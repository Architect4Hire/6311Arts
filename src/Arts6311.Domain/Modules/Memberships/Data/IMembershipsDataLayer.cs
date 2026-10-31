using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Memberships.Data;

/// <summary>Where a page of membership plans resumes: after this row in (DisplayOrder, Id) order.</summary>
internal sealed record MembershipPlanListPosition(int DisplayOrder, Guid Id);

internal enum MembershipWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another plan took the slug between Business's check and the save.</summary>
    SlugTaken = 20,
}

internal interface IMembershipsDataLayer
{
    /// <summary>One plan with its perks, tracked.</summary>
    Task<MembershipPlan?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One deleted membership plan, tracked, for a restore (ADR 0052). A live record is <c>null</c> here.</summary>
    Task<MembershipPlan?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of the Deleted view, in the same order and on the same cursor as the list.</summary>
    Task<List<MembershipPlanDeletedRow>> ListDeletedAsync(
        MembershipPlanListPosition? after, int take, string? search, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Where a new plan goes: after every existing one.</summary>
    Task<int> NextDisplayOrderAsync(CancellationToken cancellationToken);

    Task<List<MembershipPlanSummaryServiceModel>> ListAsync(
        ListSort sort,
        ListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        CancellationToken cancellationToken);

    /// <summary>Adds the plan Business built — its perks, bookkeeping and audit entry — in one save.</summary>
    Task<MembershipWriteOutcome> CreateAsync(
        MembershipPlan plan, string actorUserId, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="plan"/>, with its audit entry, in one save. No slug
    /// redirect is staged: a plan has no address of its own to redirect (ADR 0040).
    /// </summary>
    Task<MembershipWriteOutcome> UpdateAsync(
        MembershipPlan plan, string actorUserId, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a status change Business already applied, with its audit entry and <c>site.revalidate-requested</c>,
    /// in one save (ADR 0031, 0036).
    /// </summary>
    Task<MembershipWriteOutcome> TransitionAsync(
        MembershipPlan plan,
        PublishCommand command,
        PublishStatus from,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the membership plan deleted — who and when, a new row version, its audit entry and,
    /// when the site was showing it, <c>site.revalidate-requested</c> — in one save (ADR 0052).
    /// </summary>
    Task<MembershipWriteOutcome> DeleteAsync(MembershipPlan plan, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a restore Business already applied (back to Draft), clearing the delete marks, with its audit entry,
    /// in one save (ADR 0052).
    /// </summary>
    /// <param name="deletedAs">What the record was when it was deleted, for the audit line.</param>
    Task<MembershipWriteOutcome> RestoreAsync(
        MembershipPlan plan, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the scheduled publisher giving up: the Draft that Business already applied, its revalidation, and an
    /// audit row of its own saying why (ADR 0036).
    /// </summary>
    Task<MembershipWriteOutcome> AbandonScheduleAsync(
        MembershipPlan plan, PublishStatus from, string reason, CancellationToken cancellationToken);

    /// <summary>The ids of plans Scheduled for <paramref name="asOf"/> or earlier, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Every published plan with its perks, untracked, in display order.</summary>
    Task<List<MembershipPlan>> ListPublishedAsync(CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the development seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the seed plans Business chose, with one audit entry, in one save.</summary>
    Task<int> AddSeedAsync(IReadOnlyList<MembershipPlan> plans, string actor, CancellationToken cancellationToken);
}
