using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Pages.Data.Repositories;

internal interface IPagesRepository
{
    void Add(Page page);

    /// <summary>One page with its image references, tracked, for a read, an edit or a status command.</summary>
    Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One deleted page, tracked, for a restore (ADR 0052). The one read that looks past the query filter for a
    /// single row, so a live page is <c>null</c> here just as a deleted one is <c>null</c> everywhere else.
    /// </summary>
    Task<Page?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Whether another <strong>live</strong> page than <paramref name="exceptId"/> already has this slug. A deleted
    /// page holds nothing: its address is free for the next page, which is what the filtered unique index allows.
    /// </summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>
    /// Every slug in use, deleted pages included. For the placeholder seed, which adds only the pages that are
    /// missing: counting a deleted page as missing would seed the privacy policy again on the next MigrationService
    /// run, undoing the delete (ADR 0052).
    /// </summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>One page of the list in <paramref name="sort"/>'s order (ADR 0062), projected straight to list rows.</summary>
    Task<List<PageSummaryServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the Deleted view, in the same (Title, Id) order as the list above so the two share their cursor
    /// (ADR 0052).
    /// </summary>
    Task<List<PageDeletedRow>> ListDeletedAsync(
        PageListPosition? after, int take, string? search, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of pages Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036). Ids only, because the pass reads
    /// each page again before it acts on it.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>The published page at <paramref name="slug"/>, projected straight to the site's model (ADR 0042).</summary>
    Task<SitePageServiceModel?> FindPublishedAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Every published page's slug and last change, by slug, for the sitemap. No bodies.</summary>
    Task<List<(string Slug, DateTimeOffset UpdatedAt)>> ListPublishedAddressesAsync(CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
