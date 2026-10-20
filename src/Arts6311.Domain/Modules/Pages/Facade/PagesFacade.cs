using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Html;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Pages.Business;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Pages.Facade;

/// <summary>
/// Validates shape, asks Media about the pictures a body shows, then calls Business. Nothing is cached: these are CMS
/// reads; the site's cached reads arrive in Phase 3.
/// </summary>
/// <remarks>
/// Which pictures a body shows is only known once it is sanitized, so the facade runs the shared sanitizer to find
/// them — the same deterministic pass Business runs again before storing, which is what keeps the stored HTML Business's
/// to vouch for. Media is asked only whether each is an original in the library (ADR 0034).
/// </remarks>
internal sealed class PagesFacade(
    IPagesBusiness business,
    IMediaFacade media,
    IHtmlSanitizer sanitizer,
    IValidator<PageCreateViewModel> createValidator,
    IValidator<PageUpdateViewModel> updateValidator,
    IValidator<PageListViewModel> listValidator) : IPagesFacade
{
    private const string What = "this page";

    public async Task<OperationResult<CursorPage<PageSummaryServiceModel>>> ListAsync(
        PageListViewModel request, CancellationToken cancellationToken)
    {
        if (await listValidator.ValidateOrErrorAsync(request, cancellationToken) is { } error)
        {
            return OperationResult.Failure<CursorPage<PageSummaryServiceModel>>(error);
        }

        return await business.ListAsync(request, cancellationToken);
    }

    public Task<OperationResult<PageServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        business.GetAsync(id, cancellationToken);

    public async Task<OperationResult<PageServiceModel>> CreateAsync(
        PageCreateViewModel create, string actorUserId, CancellationToken cancellationToken)
    {
        if (await createValidator.ValidateOrErrorAsync(create, cancellationToken) is { } error)
        {
            return OperationResult.Failure<PageServiceModel>(error);
        }

        var images = await AskMediaAsync(create.Body, cancellationToken);
        return await business.CreateAsync(create, images, actorUserId, cancellationToken);
    }

    public async Task<OperationResult<PageServiceModel>> UpdateAsync(
        Guid id, PageUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<PageServiceModel>(preconditionError);
        }

        if (await updateValidator.ValidateOrErrorAsync(update, cancellationToken) is { } error)
        {
            return OperationResult.Failure<PageServiceModel>(error);
        }

        var images = update.Body is null ? null : await AskMediaAsync(update.Body, cancellationToken);
        return await business.UpdateAsync(id, update, ifMatch, images, actorUserId, cancellationToken);
    }

    public Task<OperationResult<PageServiceModel>> PublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Publish, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<PageServiceModel>> UnpublishAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Unpublish, ifMatch, actorUserId, cancellationToken);

    public Task<OperationResult<PageServiceModel>> ArchiveAsync(
        Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, PublishCommand.Archive, ifMatch, actorUserId, cancellationToken);

    /// <summary>A status command. Without If-Match it is a 428 before anything is read.</summary>
    private async Task<OperationResult<PageServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken)
    {
        if (EntityTag.Precondition(ifMatch, What) is { } preconditionError)
        {
            return OperationResult.Failure<PageServiceModel>(preconditionError);
        }

        return await business.TransitionAsync(id, command, ifMatch, actorUserId, cancellationToken);
    }

    /// <summary>What Media says about the library pictures <paramref name="body"/> would show once sanitized.</summary>
    private async Task<PageImageCheck> AskMediaAsync(string? body, CancellationToken cancellationToken)
    {
        var ids = sanitizer.Sanitize(body).MediaAssetIds;
        if (ids.Count == 0)
        {
            return PageImageCheck.None;
        }

        var answer = await media.CheckAttachableAsync(ids, cancellationToken);
        return new PageImageCheck(ids.ToHashSet(), answer.Missing.ToHashSet());
    }
}
