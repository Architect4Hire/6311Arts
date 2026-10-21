using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Exhibitions.Data;

/// <summary>Where a page of exhibitions resumes: after this row in (StartDate, Id) descending order.</summary>
internal sealed record ExhibitionListPosition(DateOnly StartDate, Guid Id);

internal enum ExhibitionWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another exhibition took the slug between Business's check and the save.</summary>
    SlugTaken = 20,
}

internal interface IExhibitionsDataLayer
{
    /// <summary>One exhibition with its credits, tracked.</summary>
    Task<Exhibition?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    Task<List<ExhibitionSummaryServiceModel>> ListAsync(
        ExhibitionListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>Adds the exhibition Business built, credits included, with its bookkeeping and audit entry, in one save.</summary>
    Task<ExhibitionWriteOutcome> CreateAsync(Exhibition exhibition, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="exhibition"/>, with its audit entry, in one save — and, when
    /// <paramref name="pathChange"/> is set, <c>redirects.slug-changed</c> staged before it (ADR 0035).
    /// </summary>
    Task<ExhibitionWriteOutcome> UpdateAsync(
        Exhibition exhibition, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a status change Business already applied, with its audit entry and <c>site.revalidate-requested</c>,
    /// in one save (ADR 0031, 0033, 0036).
    /// </summary>
    Task<ExhibitionWriteOutcome> TransitionAsync(
        Exhibition exhibition, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the scheduled publisher giving up: the Draft that Business already applied, its revalidation, and an
    /// audit row of its own saying why (ADR 0036).
    /// </summary>
    /// <param name="reason">The refusal staff will read, from the requirement that failed. Never a provider message.</param>
    Task<ExhibitionWriteOutcome> AbandonScheduleAsync(
        Exhibition exhibition, PublishStatus from, string reason, CancellationToken cancellationToken);

    /// <summary>The ids of exhibitions Scheduled for <paramref name="asOf"/> or earlier, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Published exhibitions with their credits, untracked: all of them, or the one with <paramref name="slug"/>.</summary>
    Task<List<Exhibition>> ListPublishedAsync(string? slug, CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the development seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the seed exhibitions Business chose, with one audit entry, in one save. Returns how many.</summary>
    Task<int> AddSeedAsync(IReadOnlyList<Exhibition> exhibitions, string actor, CancellationToken cancellationToken);
}
