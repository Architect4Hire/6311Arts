using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Pages.Facade;

/// <summary>
/// The Pages module's boundary: the CMS page routes today; the site's <c>/site/pages/{slug}</c> (Phase 3) and the
/// importer (4.x) later. Every write takes the row version as <c>ifMatch</c>.
/// </summary>
public interface IPagesFacade
{
    Task<OperationResult<CursorPage<PageSummaryServiceModel>>> ListAsync(
        PageListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<PageServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// The Deleted view (ADR 0052): the pages staff deleted, Admin only at the route. Its own cursor scope, so a
    /// cursor from the list above is not valid here.
    /// </summary>
    Task<OperationResult<CursorPage<PageDeletedServiceModel>>> ListDeletedAsync(
        PageListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the page from any status (ADR 0052): it leaves the site within the Worker's next pass, leaves the
    /// CMS list, and releases its web address. Admin only at the route (auth.md "Sensitive operations"); audited.
    /// Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>.
    /// </summary>
    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings a deleted page back as a Draft, with any schedule cleared. Admin only at the route; audited.
    /// Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.slug_taken</c>.
    /// </summary>
    Task<OperationResult<PageServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A new Draft, its body sanitized. Refusals: <c>validation.failed</c> (including a picture without alt text or not
    /// in the library), <c>cms.slug_taken</c>.
    /// </summary>
    Task<OperationResult<PageServiceModel>> CreateAsync(
        PageCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// A staff edit. Refusals: <c>validation.failed</c>, <c>resource.not_found</c>, <c>concurrency.*</c>,
    /// <c>cms.slug_taken</c>. A published slug may change: the old address is redirected (ADR 0035).
    /// </summary>
    Task<OperationResult<PageServiceModel>> UpdateAsync(
        Guid id, PageUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft → Published. Refused (<c>validation.failed</c> on <c>body</c>) while the body is empty.</summary>
    Task<OperationResult<PageServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Published → Draft. The slug stays locked.</summary>
    Task<OperationResult<PageServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Draft or Published → Archived. Admin only at the route (auth.md "Sensitive operations"); audited.</summary>
    Task<OperationResult<PageServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled, for the Worker to publish when the time comes (ADR 0036). Refusals:
    /// <c>validation.failed</c> (a time already past, or a body publishing would refuse),
    /// <c>resource.not_found</c>, <c>concurrency.*</c>, <c>cms.status_transition_invalid</c>.
    /// </summary>
    Task<OperationResult<PageServiceModel>> ScheduleAsync(
        Guid id,
        ContentScheduleViewModel schedule,
        string? ifMatch,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<PageServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher (<see cref="IScheduledPublisher"/>): the pages due at
    /// <paramref name="asOf"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// For the Worker's scheduled publisher: publishes one due page, re-read first, claimed by its row version.
    /// Never throws for a page that moved on — it reports what happened instead.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET /site/pages/{slug}</c>: the published page there, cached under the <c>page</c> type tag (ADR 0042).
    /// Anything else — a draft, an unknown or malformed slug — is <c>resource.not_found</c>, and is not cached.
    /// </summary>
    Task<OperationResult<SitePageServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>For a preview link: the page as the site would show it, whatever the status. Never cached.</summary>
    Task<OperationResult<PreviewRecord<SitePageServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>For the sitemap: every published page's address. Not cached here; the Site module caches the sitemap.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);
}
