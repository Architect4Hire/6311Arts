using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Memberships.Business;

/// <summary>
/// The membership rules. Nothing here reaches another module — a plan credits nobody and shows no library image —
/// so Business answers with the finished ServiceModels, as Pages does.
/// </summary>
internal interface IMembershipsBusiness
{
    Task<OperationResult<CursorPage<MembershipPlanSummaryServiceModel>>> ListAsync(
        MembershipPlanListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<MembershipPlanServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<MembershipPlanServiceModel>> CreateAsync(
        MembershipPlanCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<MembershipPlanServiceModel>> UpdateAsync(
        Guid id,
        MembershipPlanUpdateViewModel update,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    Task<OperationResult<MembershipPlanServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<MembershipPlanServiceModel>> ScheduleAsync(
        Guid id,
        DateTimeOffset scheduledFor,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    Task<OperationResult<MembershipPlanServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The plans due at <paramref name="asOf"/>, oldest first: the Worker's pass (ADR 0036).</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Every published tier, in display order, as <c>/memberships</c> shows it (ADR 0042).</summary>
    Task<IReadOnlyList<SiteMembershipPlanServiceModel>> ListForSiteAsync(CancellationToken cancellationToken);

    /// <summary>One tier as the site would show it, whatever the status, for a preview link.</summary>
    Task<OperationResult<PreviewRecord<SiteMembershipPlanServiceModel>>> GetForPreviewAsync(
        Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// <c>/memberships</c>, once, when any tier is published — a plan has no address of its own (ADR 0040) — dated
    /// by the latest change among them.
    /// </summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);

    /// <summary>Publishes one due plan, read again inside this scope and claimed by its row version (ADR 0036).</summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>Adds the development tiers that are missing, skipping any slug already in use.</summary>
    Task<int> SeedAsync(IReadOnlyList<MembershipPlanSeed> seeds, string actor, CancellationToken cancellationToken);
}
