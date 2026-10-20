using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Artists.Data;

/// <summary>Where a roster page resumes: after this row in (Name, Id) order.</summary>
internal sealed record ArtistListPosition(string Name, Guid Id);

internal enum ArtistWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another artist took the slug between Business's check and the save.</summary>
    SlugTaken = 20,
}

internal interface IArtistsDataLayer
{
    Task<Artist?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    Task<List<ArtistSummaryServiceModel>> ListAsync(
        ArtistListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    Task<List<ArtistSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<List<ArtistSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>Adds the artist Business built, with its bookkeeping and audit entry, in one save.</summary>
    Task<ArtistWriteOutcome> CreateAsync(Artist artist, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="artist"/>, with its audit entry, in one save — and, when
    /// <paramref name="pathChange"/> is set, <c>redirects.slug-changed</c> staged before it (ADR 0035).
    /// </summary>
    Task<ArtistWriteOutcome> UpdateAsync(
        Artist artist, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a status change Business already applied, with its audit entry, in one save. From 2.8 this is also where
    /// <c>site.revalidate-requested</c> is staged (ADR 0031).
    /// </summary>
    Task<ArtistWriteOutcome> TransitionAsync(
        Artist artist, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the development seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the seed artists Business chose, with one audit entry, in one save. Returns how many.</summary>
    Task<int> AddSeedAsync(IReadOnlyList<Artist> artists, string actor, CancellationToken cancellationToken);
}
