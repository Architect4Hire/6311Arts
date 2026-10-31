using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Instructors.Data;

/// <summary>Where a list page resumes: after this row in (Name, Id) order.</summary>
internal sealed record InstructorListPosition(string Name, Guid Id);

internal enum InstructorWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another instructor took the slug between Business's check and the save.</summary>
    SlugTaken = 20,
}

internal interface IInstructorsDataLayer
{
    Task<Instructor?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// What to call the live records this module holds that name artist <paramref name="artistId"/>, at most
    /// <paramref name="take"/> of them: the answer this module's <c>IContentReferenceSource</c> gives the kernel
    /// before that artist is deleted (ADR 0052).
    /// </summary>
    Task<List<string>> ListNamingArtistAsync(Guid artistId, int take, CancellationToken cancellationToken);

    /// <summary>One deleted instructor, tracked, for a restore (ADR 0052). A live record is <c>null</c> here.</summary>
    Task<Instructor?> FindDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of the Deleted view, in the same order and on the same cursor as the list.</summary>
    Task<List<InstructorDeletedRow>> ListDeletedAsync(
        InstructorListPosition? after, int take, string? search, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    Task<List<InstructorSummaryServiceModel>> ListAsync(
        ListSort sort, ListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    Task<List<InstructorSummaryServiceModel>> GetSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<List<InstructorSummaryServiceModel>> FindBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>Published instructors as site rows: all of them, or the one with <paramref name="slug"/>.</summary>
    Task<List<InstructorSiteRow>> ListPublishedAsync(string? slug, CancellationToken cancellationToken);

    /// <summary>Adds the instructor Business built, with its bookkeeping and audit entry, in one save.</summary>
    Task<InstructorWriteOutcome> CreateAsync(Instructor instructor, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="instructor"/>, with its audit entry, in one save — and, when
    /// <paramref name="pathChange"/> is set, <c>redirects.slug-changed</c> staged before it (ADR 0035).
    /// </summary>
    Task<InstructorWriteOutcome> UpdateAsync(
        Instructor instructor, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a status change Business already applied, with its audit entry and <c>site.revalidate-requested</c>,
    /// in one save (ADR 0031, 0036).
    /// </summary>
    Task<InstructorWriteOutcome> TransitionAsync(
        Instructor instructor, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the instructor deleted — who and when, a new row version, its audit entry and,
    /// when the site was showing it, <c>site.revalidate-requested</c> — in one save (ADR 0052).
    /// </summary>
    Task<InstructorWriteOutcome> DeleteAsync(Instructor instructor, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a restore Business already applied (back to Draft), clearing the delete marks, with its audit entry,
    /// in one save (ADR 0052).
    /// </summary>
    /// <param name="deletedAs">What the record was when it was deleted, for the audit line.</param>
    Task<InstructorWriteOutcome> RestoreAsync(
        Instructor instructor, PublishStatus deletedAs, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the scheduled publisher giving up: the Draft that Business already applied, its revalidation, and an
    /// audit row of its own saying why (ADR 0036).
    /// </summary>
    /// <param name="reason">The refusal staff will read, from the requirement that failed. Never a provider message.</param>
    Task<InstructorWriteOutcome> AbandonScheduleAsync(
        Instructor instructor, PublishStatus from, string reason, CancellationToken cancellationToken);

    /// <summary>The ids of instructors Scheduled for <paramref name="asOf"/> or earlier, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the development seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the seed instructors Business chose, with one audit entry, in one save. Returns how many.</summary>
    Task<int> AddSeedAsync(IReadOnlyList<Instructor> instructors, string actor, CancellationToken cancellationToken);
}
