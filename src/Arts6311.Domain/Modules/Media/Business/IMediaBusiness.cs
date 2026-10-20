using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Media.Business;

internal interface IMediaBusiness
{
    Task<OperationResult<MediaAssetServiceModel>> UploadAsync(
        MediaUploadViewModel upload, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<CursorPage<MediaAssetSummaryServiceModel>>> ListAsync(
        MediaListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<MediaAssetServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<MediaAssetServiceModel>> UpdateAsync(
        Guid id, MediaAssetUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<MediaContentServiceModel>> OpenAsync(
        MediaDeliveryViewModel request, MediaViewer viewer, CancellationToken cancellationToken);

    Task<MediaAttachabilityServiceModel> CheckAttachableAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<OperationResult> ProduceRenditionsAsync(
        Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken);

    Task PurgeAsync(Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken);
}
