using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Pages.Data;

/// <summary>Where a list page resumes: after this row in (Title, Id) order.</summary>
internal sealed record PageListPosition(string Title, Guid Id);

internal enum PageWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another page took the slug between Business's check and the save.</summary>
    SlugTaken = 20,
}

internal interface IPagesDataLayer
{
    /// <summary>One page with its image references, tracked.</summary>
    Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One deleted page, tracked, for a restore (ADR 0052). A live page is <c>null</c> here.</summary>
    Task<Page?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    Task<List<PageSummaryServiceModel>> ListAsync(
        PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>One page of the Deleted view, in the same order and on the same cursor as the list.</summary>
    Task<List<PageDeletedRow>> ListDeletedAsync(
        PageListPosition? after, int take, string? search, CancellationToken cancellationToken);

    /// <summary>Adds the page Business built, image references included, with its bookkeeping and audit entry, in one save.</summary>
    Task<PageWriteOutcome> CreateAsync(Page page, string actorUserId, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="page"/>, with its audit entry, in one save — and, when
    /// <paramref name="pathChange"/> is set, <c>redirects.slug-changed</c> staged before it (ADR 0035).
    /// </summary>
    Task<PageWriteOutcome> UpdateAsync(
        Page page, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a status change Business already applied, with its audit entry and <c>site.revalidate-requested</c>,
    /// in one save (ADR 0031, 0034, 0036).
    /// </summary>
    Task<PageWriteOutcome> TransitionAsync(
        Page page, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the page deleted — who and when, a new row version, its audit entry and, when the site was showing
    /// it, <c>site.revalidate-requested</c> — in one save (ADR 0052).
    /// </summary>
    Task<PageWriteOutcome> DeleteAsync(Page page, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a restore Business already applied (back to Draft), clearing the delete marks, with its audit entry,
    /// in one save (ADR 0052). Nothing is revalidated: the page comes back as a Draft, which the site never shows.
    /// </summary>
    /// <param name="deletedAs">What the page was when it was deleted, for the audit line.</param>
    Task<PageWriteOutcome> RestoreAsync(
        Page page, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the scheduled publisher giving up: the Draft that Business already applied, its revalidation, and an
    /// audit row of its own saying why (ADR 0036).
    /// </summary>
    /// <param name="reason">The refusal staff will read, from the requirement that failed. Never a provider message.</param>
    Task<PageWriteOutcome> AbandonScheduleAsync(
        Page page, PublishStatus from, string reason, CancellationToken cancellationToken);

    /// <summary>The ids of pages Scheduled for <paramref name="asOf"/> or earlier, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>The published page at <paramref name="slug"/>, as the site shows it.</summary>
    Task<SitePageServiceModel?> FindPublishedAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Every published page's slug and last change, for the sitemap.</summary>
    Task<List<(string Slug, DateTimeOffset UpdatedAt)>> ListPublishedAddressesAsync(CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the placeholder seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the placeholder pages Business chose, with one audit entry, in one save. Returns how many.</summary>
    Task<int> AddSeedAsync(IReadOnlyList<Page> pages, string actor, CancellationToken cancellationToken);
}
