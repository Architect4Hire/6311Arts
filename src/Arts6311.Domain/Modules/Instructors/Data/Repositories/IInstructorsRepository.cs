using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Instructors.Data.Repositories;

internal interface IInstructorsRepository
{
    void Add(Instructor instructor);

    /// <summary>One instructor, tracked, for an edit or a status command.</summary>
    Task<Instructor?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether another instructor than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use. For the development seed, which adds only the instructors that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>One page of the instructors in (Name, Id) order, projected straight to list rows.</summary>
    Task<List<InstructorSummaryServiceModel>> ListAsync(
        InstructorListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>The instructors with these ids, projected to list rows, in no particular order.</summary>
    Task<List<InstructorSummaryServiceModel>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>The instructors with these slugs, projected to list rows, in no particular order.</summary>
    Task<List<InstructorSummaryServiceModel>> ListBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>
    /// Published instructors in (Name, Id) order, projected straight to site rows — every one, or only the one with
    /// <paramref name="slug"/> (ADR 0042).
    /// </summary>
    Task<List<InstructorSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken);

    /// <summary>
    /// The ids of instructors Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036). Ids only, because the pass reads
    /// each instructor again before it acts on it.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
