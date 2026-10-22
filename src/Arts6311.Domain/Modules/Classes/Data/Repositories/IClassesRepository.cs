using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;

namespace Arts6311.Domain.Modules.Classes.Data.Repositories;

internal interface IClassesRepository
{
    void Add(ClassOffering offering);

    /// <summary>One offering with its runs, their sessions and its body images, tracked, for a read, an edit or a status command.</summary>
    Task<ClassOffering?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One deleted class, tracked, for a restore (ADR 0052). The one read that looks past the query filter for
    /// a single row, so a live record is <c>null</c> here just as a deleted one is <c>null</c> everywhere else.
    /// </summary>
    Task<ClassOffering?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the Deleted view, in the same order as the list above so the two share their cursor (ADR 0052).
    /// </summary>
    Task<List<ClassDeletedRow>> ListDeletedAsync(
        ClassListPosition? after, int take, string? search, CancellationToken cancellationToken);

    /// <summary>Whether another offering than <paramref name="exceptId"/> already has this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Every slug in use. For the development seed, which adds only the offerings that are missing.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The number a new offering takes: one past the highest in use, for its runs' suggested SKUs (ADR 0037).
    /// A suggestion aid, so a race that hands two offerings the same number costs nothing.
    /// </summary>
    Task<int> NextOfferingNumberAsync(CancellationToken cancellationToken);

    /// <summary>
    /// One page in (Title, Id) order, projected straight to list rows.
    /// </summary>
    /// <param name="asOf">Now, so each row can say when the offering next meets.</param>
    Task<List<ClassListRow>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// The ids of offerings Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/>: the Worker's scheduled-publish pass (ADR 0036). Ids only, because the pass reads
    /// each offering again before it acts on it.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Published offerings with their runs and sessions, untracked — every one, or only the one with
    /// <paramref name="slug"/>; and, given <paramref name="endingAfter"/>, only those with a session that ends after it
    /// (ADR 0042).
    /// </summary>
    Task<List<ClassOffering>> ListPublishedAsync(string? slug, DateTimeOffset? endingAfter, CancellationToken cancellationToken);

    /// <summary>
    /// What to call the live records that name instructor <paramref name="instructorId"/>, at most
    /// <paramref name="take"/> of them: the classes whose runs name an instructor (ADR 0044). The query filter leaves out records that are themselves
    /// deleted, which is what makes a deleted one hold nothing up (ADR 0052).
    /// </summary>
    Task<List<string>> ListNamingInstructorAsync(Guid instructorId, int take, CancellationToken cancellationToken);

    /// <summary>Throws away every unsaved change, after a save that failed, so the next save starts clean.</summary>
    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
