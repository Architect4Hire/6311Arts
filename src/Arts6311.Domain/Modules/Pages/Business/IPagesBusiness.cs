using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Pages.Business;

internal interface IPagesBusiness
{
    Task<OperationResult<CursorPage<PageSummaryServiceModel>>> ListAsync(
        PageListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<PageServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The Deleted view: the pages staff deleted, newest cursor rules as the list (ADR 0052).</summary>
    Task<OperationResult<CursorPage<PageDeletedServiceModel>>> ListDeletedAsync(
        PageListViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the page, whatever its status: it leaves the site, leaves the CMS list, and releases its web
    /// address, and only the Deleted view and the audit trail still name it (ADR 0052). Refusals:
    /// <c>resource.not_found</c>, <c>concurrency.*</c>.
    /// </summary>
    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Brings a deleted page back as a Draft, with any schedule cleared: nothing returns to the site by being
    /// restored (ADR 0052). Refusals: <c>resource.not_found</c>, <c>concurrency.*</c>, and
    /// <c>cms.slug_taken</c> when another page took its web address while it was deleted.
    /// </summary>
    Task<OperationResult<PageServiceModel>> RestoreAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The published page at <paramref name="slug"/>, or <c>resource.not_found</c>: a draft is not there to the site.</summary>
    Task<OperationResult<SitePageServiceModel>> GetForSiteAsync(string slug, CancellationToken cancellationToken);

    /// <summary>One page as the site would show it, whatever the status, for a preview link.</summary>
    Task<OperationResult<PreviewRecord<SitePageServiceModel>>> GetForPreviewAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every published page's address and last change, for the sitemap.</summary>
    Task<IReadOnlyList<SitemapEntryServiceModel>> ListSitemapAsync(CancellationToken cancellationToken);

    /// <param name="images">What Media said about the library images in <c>create.Body</c>.</param>
    Task<OperationResult<PageServiceModel>> CreateAsync(
        PageCreateViewModel create, PageImageCheck images, string actorUserId, CancellationToken cancellationToken);

    /// <param name="images">What Media said about the library images in <c>update.Body</c>, when a body is sent.</param>
    Task<OperationResult<PageServiceModel>> UpdateAsync(
        Guid id,
        PageUpdateViewModel update,
        string? ifMatch,
        PageImageCheck? images,
        string actorUserId,
        CancellationToken cancellationToken);

    Task<OperationResult<PageServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Draft or Scheduled → Scheduled for <paramref name="scheduledFor"/>. Refusals: <c>concurrency.*</c>,
    /// <c>cms.status_transition_invalid</c>, and <c>validation.failed</c> for a time already past or a body
    /// publishing would refuse (ADR 0036). Its own method, because <c>schedule</c> carries a body.
    /// </summary>
    Task<OperationResult<PageServiceModel>> ScheduleAsync(
        Guid id, DateTimeOffset scheduledFor, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Scheduled → Draft, with the time cleared.</summary>
    Task<OperationResult<PageServiceModel>> UnscheduleAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The pages due to be published at <paramref name="asOf"/>, for the Worker's scheduled publisher.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one due page on the scheduled publisher's behalf, re-reading it first: the row version is the
    /// claim, so two Workers acting on one page produce one publish and one
    /// <see cref="ScheduledPublishOutcome.Lost"/>.
    /// </summary>
    Task<ScheduledPublishOutcome> PublishScheduledAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);

    /// <summary>
    /// The placeholder pages: adds each, as a Draft, whose slug is not in use yet. Returns how many were added. Never
    /// touches an existing row, so the real policy a staff member wrote survives every rerun.
    /// </summary>
    Task<int> SeedAsync(IReadOnlyList<PagePlaceholder> placeholders, string actor, CancellationToken cancellationToken);
}
