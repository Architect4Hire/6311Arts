using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Classes.Data.Repositories;

internal interface IClassesRepository
{
    void Add(ClassOffering offering);

    /// <summary>One offering with its sessions and body images, tracked, for a read, an edit or a status command.</summary>
    Task<ClassOffering?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another offering than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use. For the development seed, which adds only the offerings that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The number a new offering takes: one past the highest in use, for its sessions' suggested SKUs (ADR 0037).
    /// A suggestion aid, so a race that hands two offerings the same number costs nothing.
    /// </summary>
    Task<int> NextOfferingNumberAsync(CancellationToken cancellationToken);

    /// <summary>
    /// One page in (Title, Id) order, projected straight to list rows.
    /// </summary>
    /// <param name="asOf">Now, so each row can say when the offering next meets.</param>
    Task<List<ClassSummaryServiceModel>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// The ids of offerings Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036). Ids only, because the pass reads
    /// each offering again before it acts on it.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
