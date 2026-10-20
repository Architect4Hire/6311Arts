using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Pages.Business;

internal interface IPagesBusiness
{
    Task<OperationResult<CursorPage<PageSummaryServiceModel>>> ListAsync(
        PageListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<PageServiceModel>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <param name="images">What Media said about the library images in <c>create.Body</c>.</param>
    Task<OperationResult<PageServiceModel>> CreateAsync(
        PageCreateViewModel create, PageImageCheck images, string actorUserId, CancellationToken cancellationToken);

    /// <param name="images">What Media said about the library images in <c>update.Body</c>, when a body is sent.</param>
    Task<OperationResult<PageServiceModel>> UpdateAsync(
        Guid id,
        PageUpdateViewModel update,
        string? ifMatch,
        PageImageCheck? images,
        string actorUserId,
        CancellationToken cancellationToken);

    Task<OperationResult<PageServiceModel>> TransitionAsync(
        Guid id, PublishCommand command, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// The placeholder pages: adds each, as a Draft, whose slug is not in use yet. Returns how many were added. Never
    /// touches an existing row, so the real policy a staff member wrote survives every rerun.
    /// </summary>
    Task<int> SeedAsync(IReadOnlyList<PagePlaceholder> placeholders, string actor, CancellationToken cancellationToken);
}
