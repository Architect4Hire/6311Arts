using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Media.Facade;

/// <summary>
/// The Media module's boundary: the CMS media routes, <c>/media/**</c> delivery, this module's outbox handlers and,
/// from 2.4, the content modules (which ask whether an image may go on published content).
/// </summary>
public interface IMediaFacade
{
    /// <summary>
    /// Stores an upload as a compressed master and stages its renditions. Refusals:
    /// <c>media.too_large</c>, <c>media.type_unsupported</c>, <c>media.extension_mismatch</c>,
    /// <c>media.dimensions_out_of_range</c>, <c>media.undecodable</c>, <c>media.duplicate</c>.
    /// </summary>
    Task<OperationResult<MediaAssetServiceModel>> UploadAsync(
        MediaUploadViewModel upload, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<CursorPage<MediaAssetSummaryServiceModel>>> ListAsync(
        MediaListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<MediaAssetServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Alt text, credit and preset. Requires the ETag as <paramref name="ifMatch"/>.</summary>
    Task<OperationResult<MediaAssetServiceModel>> UpdateAsync(
        Guid id, MediaAssetUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Refused with <c>media.in_use</c> while anything references the asset.</summary>
    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>An asset's bytes for <c>/media/**</c>. A draft answers <c>resource.not_found</c> to anyone but staff.</summary>
    Task<OperationResult<MediaContentServiceModel>> OpenAsync(
        MediaDeliveryViewModel request, MediaViewer viewer, CancellationToken cancellationToken);

    /// <summary>For content modules: which of these images are missing, or have no alt text yet (media.md).</summary>
    Task<MediaAttachabilityServiceModel> CheckAttachableAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>
    /// For content modules' site reads (3.1): each of these images that has renditions, as the site shows it. An id
    /// with no original, or no rendition yet, is simply absent (ADR 0042).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, SiteImageServiceModel>> DescribeForSiteAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Worker: produces an original's renditions as it stands now. Fails only with <c>media.undecodable</c>.</summary>
    Task<OperationResult> ProduceRenditionsAsync(
        Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken);

    /// <summary>Worker: deletes a deleted asset's blobs.</summary>
    Task PurgeAsync(Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken);
}
