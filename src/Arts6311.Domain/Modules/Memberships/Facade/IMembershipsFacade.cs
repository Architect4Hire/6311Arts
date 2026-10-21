using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Memberships.Managers.ServiceModels;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Memberships.Facade;

/// <summary>
/// The Memberships module's boundary: the CMS membership routes today; the site's <c>/site/memberships</c>
/// (Phase 3), the Square plan push (5.x), member signup (5.6) and the importer (4.x) later. Every write to an
/// existing plan takes the row version as <c>ifMatch</c>; a create has no version to carry yet.
/// </summary>
public interface IMembershipsFacade
{
    Task<OperationResult<CursorPage<MembershipPlanSummaryServiceModel>>> ListAsync(
        MembershipPlanListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<MembershipPlanServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>A new Draft. Refusals: <c>validation.failed</c>, <c>cms.slug_taken</c>.</summary>
    Task<OperationResult<MembershipPlanServiceModel>> CreateAsync(
        MembershipPlanCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A staff edit. Refusals: <c>validation.failed</c> (including a starting price or cadence Square now holds),
    /// <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.slug_taken</c>. A slug change redirects nothing: a
    /// plan has no address of its own (ADR 0040).
    /// </summary>
    Task<OperationResult<MembershipPlanServiceModel>> UpdateAsync(
        Guid id,
        MembershipPlanUpdateViewModel update,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Draft → Published. Refused (<c>validation.failed</c>) without a price or at least one perk.</summary>
    Task<OperationResult<MembershipPlanServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Published → Draft. The slug stays as it is.</summary>
    Task<OperationResult<MembershipPlanServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft or Published → Archived. Admin only at the route (auth.md "Sensitive operations"); audited.</summary>
    Task<OperationResult<MembershipPlanServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled, for the Worker to publish when the time comes (ADR 0036). Refusals:
    /// <c>validation.failed</c> (a time already past, or anything publishing would refuse),
    /// <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.status_transition_invalid</c>.
    /// </summary>
    Task<OperationResult<MembershipPlanServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<MembershipPlanServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher (<see cref="IScheduledPublisher"/>): the plans due at
    /// <paramref name="asOf"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher: publishes one due plan, re-read first, claimed by its row version.
    /// Never throws for a plan that moved on — it reports what happened instead.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /site/memberships</c>: every published tier, in display order, cached under the
    /// <c>membership-plan</c> type tag (ADR 0042). A small fixed list, so not paged.
    /// </summary>
    Task<OperationResult<IReadOnlyList<SiteMembershipPlanServiceModel>>> ListForSiteAsync(CancellationToken cancellationToken);

    /// <summary>For a preview link: the tier as the site would show it, whatever the status. Never cached.</summary>
    Task<OperationResult<PreviewRecord<SiteMembershipPlanServiceModel>>> GetForPreviewAsync(
        Guid id, CancellationToken cancellationToken);

    /// <summary>For the sitemap. Not cached here; the Site module caches the sitemap.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);
}
