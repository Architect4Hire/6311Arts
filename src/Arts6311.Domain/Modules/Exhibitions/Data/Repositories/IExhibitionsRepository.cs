using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Exhibitions.Data.Repositories;

internal interface IExhibitionsRepository
{
    void Add(Exhibition exhibition);

    /// <summary>One exhibition with its credits, tracked, for a read, an edit or a status command.</summary>
    Task<Exhibition?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another exhibition than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use. For the development seed, which adds only the exhibitions that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>One page in (StartDate, Id) descending order, projected straight to list rows.</summary>
    Task<List<ExhibitionSummaryServiceModel>> ListAsync(
        ExhibitionListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of exhibitions Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036). Ids only, because the pass reads
    /// each exhibition again before it acts on it.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
