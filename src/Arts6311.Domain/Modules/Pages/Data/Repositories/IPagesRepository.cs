using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Pages.Data.Repositories;

internal interface IPagesRepository
{
    void Add(Page page);

    /// <summary>One page with its image references, tracked, for a read, an edit or a status command.</summary>
    Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another page than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use. For the placeholder seed, which adds only the pages that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>One page of the list in (Title, Id) order, projected straight to list rows.</summary>
    Task<List<PageSummaryServiceModel>> ListAsync(
        PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of pages Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036). Ids only, because the pass reads
    /// each page again before it acts on it.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
