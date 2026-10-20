using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Artists.Data.Repositories;

internal interface IArtistsRepository
{
    void Add(Artist artist);

    /// <summary>One artist, tracked, for an edit or a status command.</summary>
    Task<Artist?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another artist than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use. For the development seed, which adds only the artists that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>One page of the roster in (Name, Id) order, projected straight to list rows.</summary>
    Task<List<ArtistSummaryServiceModel>> ListAsync(
        ArtistListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
