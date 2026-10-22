using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Exhibitions.Data.Repositories;

internal interface IExhibitionsRepository
{
    void Add(Exhibition exhibition);

    /// <summary>One exhibition with its credits, tracked, for a read, an edit or a status command.</summary>
    Task<Exhibition?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One deleted exhibition, tracked, for a restore (ADR 0052). The one read that looks past the query filter for
    /// a single row, so a live record is <c>null</c> here just as a deleted one is <c>null</c> everywhere else.
    /// </summary>
    Task<Exhibition?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the Deleted view, in the same order as the list above so the two share their cursor (ADR 0052).
    /// </summary>
    Task<List<ExhibitionDeletedRow>> ListDeletedAsync(
        ExhibitionListPosition? after, int take, string? search, CancellationToken cancellationToken);

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

    /// <summary>
    /// Published exhibitions with their credits, untracked, newest opening first — every one, or only the one with
    /// <paramref name="slug"/> (ADR 0042).
    /// </summary>
    Task<List<Exhibition>> ListPublishedAsync(string? slug, CancellationToken cancellationToken);

    /// <summary>
    /// What to call the live records that name artist <paramref name="artistId"/>, at most
    /// <paramref name="take"/> of them: the exhibitions that credit an artist (ADR 0033). The query filter leaves out records that are themselves
    /// deleted, which is what makes a deleted one hold nothing up (ADR 0052).
    /// </summary>
    Task<List<string>> ListNamingArtistAsync(Guid artistId, int take, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
