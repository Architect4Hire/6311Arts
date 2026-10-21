using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Media.Business;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Media.Facade;

/// <summary>
/// Validates shape, then calls Business once. Nothing is cached here: delivery reads one indexed row, and its
/// responses carry long-lived cache headers, so browsers and any CDN do the caching (ADR 0030).
/// </summary>
internal sealed class MediaFacade(
    IMediaBusiness business,
    IValidator<MediaUploadViewModel> uploadValidator,
    IValidator<MediaAssetUpdateViewModel> updateValidator,
    IValidator<MediaListViewModel> listValidator) : IMediaFacade
{
    public async Task<OperationResult<MediaAssetServiceModel>> UploadAsync(
        MediaUploadViewModel upload, string actorUserId, CancellationToken cancellationToken)
    {
        if (await uploadValidator.ValidateOrErrorAsync(upload, cancellationToken) is { } error)
        {
            return OperationResult.Failure<MediaAssetServiceModel>(error);
        }

        return await business.UploadAsync(upload, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<CursorPage<MediaAssetSummaryServiceModel>>> ListAsync(
        MediaListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<MediaAssetSummaryServiceModel>>(error);
        }

        return await business.ListAsync(request, cancellationToken);
    }

    public Task<OperationResult<MediaAssetServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        business.GetAsync(id, cancellationToken);

    public async Task<OperationResult<MediaAssetServiceModel>> UpdateAsync(
        Guid id, MediaAssetUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<MediaAssetServiceModel>(error);
        }

        return await business.UpdateAsync(id, update, ifMatch, actorUserId, cancellationToken);
    }

    public Task<OperationResult> DeleteAsync(Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        business.DeleteAsync(id, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<MediaContentServiceModel>> OpenAsync(
        MediaDeliveryViewModel request, MediaViewer viewer, CancellationToken cancellationToken) =>
        business.OpenAsync(request, viewer, cancellationToken);

    public Task<MediaAttachabilityServiceModel> CheckAttachableAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        business.CheckAttachableAsync(ids, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, SiteImageServiceModel>> DescribeForSiteAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        business.DescribeForSiteAsync(ids, cancellationToken);

    public Task<OperationResult> ProduceRenditionsAsync(
        Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken) =>
        business.ProduceRenditionsAsync(assetId, attempt, lastAttempt, cancellationToken);

    public Task PurgeAsync(Guid assetId, int attempt, bool lastAttempt, CancellationToken cancellationToken) =>
        business.PurgeAsync(assetId, attempt, lastAttempt, cancellationToken);
}
