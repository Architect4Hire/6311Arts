using Arts6311.Domain.Modules.Media.Data;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Media.Data.Repositories;

internal interface IMediaRepository
{
    void Add(MediaAsset asset);

    void Remove(MediaAsset asset);

    /// <summary>An original or imported asset, tracked; never a rendition.</summary>
    Task<MediaAsset?> FindOriginalAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>An original's renditions, tracked.</summary>
    Task<List<MediaAsset>> GetRenditionsAsync(Guid originalId, CancellationToken cancellationToken);

    /// <summary>The original already holding exactly these bytes, if any.</summary>
    Task<Guid?> FindOriginalByChecksumAsync(string checksum, CancellationToken cancellationToken);

    /// <summary>One page of originals, newest first, projected straight to tiles.</summary>
    Task<List<MediaAssetSummaryServiceModel>> ListOriginalsAsync(
        MediaListPosition? after, int take, bool missingAltText, MediaProcessingStatus? status, CancellationToken cancellationToken);

    /// <summary>The ids among <paramref name="ids"/> that exist as originals, with whether each has alt text.</summary>
    Task<List<(Guid Id, bool HasAltText)>> FindAttachabilityAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// The originals among <paramref name="ids"/> and every rendition of each, untracked, for the site's picture
    /// descriptions (ADR 0042).
    /// </summary>
    Task<List<MediaAsset>> FindWithRenditionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// Whether anything references the asset: every foreign key in the model whose principal is
    /// <see cref="MediaAsset"/>, other than a rendition's own link to its original.
    /// </summary>
    Task<bool> IsReferencedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The original itself (<c>rendition: null</c>) or one of its renditions, with the original's visibility.</summary>
    Task<MediaDeliveryRecord?> FindDeliveryAsync(Guid id, string? rendition, CancellationToken cancellationToken);

    /// <summary>Every blob key a row names for this asset: the original's and its renditions'.</summary>
    Task<HashSet<string>> GetBlobKeysAsync(Guid assetId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets an original's processing status straight in the database, without loading or row-version checking it:
    /// the Worker's bookkeeping must never collide with a staff edit. Returns the rows updated (0 when it is gone).
    /// </summary>
    Task<int> SetProcessingStatusAsync(Guid originalId, MediaProcessingStatus status, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
