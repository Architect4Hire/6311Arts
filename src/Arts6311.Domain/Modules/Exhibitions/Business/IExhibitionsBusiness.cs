using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Exhibitions.Business;

/// <summary>
/// The exhibition rules. Returns <see cref="ExhibitionDetail"/> rather than the finished ServiceModel: who the
/// credited artists are is the Artists module's to say, and only the facade may ask it.
/// </summary>
internal interface IExhibitionsBusiness
{
    Task<OperationResult<CursorPage<ExhibitionSummaryServiceModel>>> ListAsync(
        ExhibitionListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ExhibitionDetail>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The facade has already confirmed the hero is in the library and every credited artist is on the roster.</summary>
    Task<OperationResult<ExhibitionDetail>> CreateAsync(
        ExhibitionCreateViewModel create, string actorUserId, CancellationToken cancellationToken);

    /// <param name="hero">What Media said about <c>update.HeroMediaId</c>, when one is sent.</param>
    Task<OperationResult<ExhibitionDetail>> UpdateAsync(
        Guid id,
        ExhibitionUpdateViewModel update,
        string? ifMatch,
        ExhibitionHeroCheck? hero,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <param name="hero">Required for <see cref="PublishCommand.Publish"/>: what Media said about the hero.</param>
    Task<OperationResult<ExhibitionDetail>> TransitionAsync(
        Guid id,
        PublishCommand command,
        string? ifMatch,
        ExhibitionHeroCheck? hero,
        string actorUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The development shows: adds, as Published and without heroes, each one whose slug is not in use yet. Returns
    /// how many were added. Never touches an existing row, so a staff edit survives a rerun.
    /// </summary>
    Task<int> SeedAsync(IReadOnlyList<ExhibitionSeed> seeds, string actor, CancellationToken cancellationToken);
}
