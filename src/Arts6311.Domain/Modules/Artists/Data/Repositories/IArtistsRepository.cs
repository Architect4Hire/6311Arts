using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Artists.Data.Repositories;

internal interface IArtistsRepository
{
    void Add(Artist artist);

    /// <summary>One artist, tracked, for an edit or a status command.</summary>
    Task<Artist?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One deleted artist, tracked, for a restore (ADR 0052). The one read that looks past the query filter for
    /// a single row, so a live record is <c>null</c> here just as a deleted one is <c>null</c> everywhere else.
    /// </summary>
    Task<Artist?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the Deleted view, in the same order as the list above so the two share their cursor (ADR 0052).
    /// </summary>
    Task<List<ArtistDeletedRow>> ListDeletedAsync(
        ArtistListPosition? after, int take, string? search, CancellationToken cancellationToken);

    /// <summary>Whether another artist than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use. For the development seed, which adds only the artists that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>One page of the roster in (Name, Id) order, projected straight to list rows.</summary>
    Task<List<ArtistSummaryServiceModel>> ListAsync(
        ArtistListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>The artists with these ids, projected to list rows, in no particular order.</summary>
    Task<List<ArtistSummaryServiceModel>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The artists with these slugs, projected to list rows, in no particular order.</summary>
    Task<List<ArtistSummaryServiceModel>> ListBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>
    /// Published artists in (Name, Id) order, projected straight to site rows — every one, or only the one with
    /// <paramref name="slug"/> (ADR 0042).
    /// </summary>
    Task<List<ArtistSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of artists Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036). Ids only, because the pass reads
    /// each artist again before it acts on it.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
