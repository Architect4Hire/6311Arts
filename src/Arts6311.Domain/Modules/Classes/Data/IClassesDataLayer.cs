using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Managers;

namespace Arts6311.Domain.Modules.Classes.Data;

/// <summary>Where a page of offerings resumes: after this row in (Title, Id) order.</summary>
internal sealed record ClassListPosition(string Title, Guid Id);

internal enum ClassWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another offering took the slug between Business's check and the save.</summary>
    SlugTaken = 20,

    /// <summary>Another record took one of the SKUs between Business's check and the save (ADR 0037).</summary>
    SkuTaken = 30,
}

internal interface IClassesDataLayer
{
    /// <summary>One offering with its runs, their sessions and its body images, tracked.</summary>
    Task<ClassOffering?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Who holds each of <paramref name="skus"/> now, so Business can refuse one another record has.</summary>
    Task<IReadOnlyDictionary<string, SkuOwner>> SkuOwnersAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken);

    /// <summary>The number a new offering takes, for its runs' suggested SKUs (ADR 0037).</summary>
    Task<int> NextOfferingNumberAsync(CancellationToken cancellationToken);

    Task<List<ClassListRow>> ListAsync(
        ClassListPosition? after,
        int take,
        PublishStatus? status,
        string? search,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds the offering Business built — runs, sessions, body images, SKU reservations, bookkeeping and audit entry —
    /// in one save.
    /// </summary>
    Task<ClassWriteOutcome> CreateAsync(
        ClassOffering offering, SkuChanges skus, string actorUserId, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="offering"/>, with its SKU reservations and releases and
    /// its audit entry, in one save — and, when <paramref name="pathChange"/> is set, <c>redirects.slug-changed</c>
    /// staged before it (ADR 0035).
    /// </summary>
    Task<ClassWriteOutcome> UpdateAsync(
        ClassOffering offering,
        SkuChanges skus,
        string actorUserId,
        string summary,
        PublishedPathChange? pathChange,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves a status change Business already applied, with its audit entry and <c>site.revalidate-requested</c>,
    /// in one save (ADR 0031, 0036).
    /// </summary>
    Task<ClassWriteOutcome> TransitionAsync(
        ClassOffering offering, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the scheduled publisher giving up: the Draft that Business already applied, its revalidation, and an
    /// audit row of its own saying why (ADR 0036).
    /// </summary>
    /// <param name="reason">The refusal staff will read, from the requirement that failed. Never a provider message.</param>
    Task<ClassWriteOutcome> AbandonScheduleAsync(
        ClassOffering offering, PublishStatus from, string reason, CancellationToken cancellationToken);

    /// <summary>The ids of offerings Scheduled for <paramref name="asOf"/> or earlier, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Published offerings with their runs and sessions, untracked: all of them or the one at <paramref name="slug"/>, and
    /// only those with a session ending after <paramref name="endingAfter"/> when it is given.
    /// </summary>
    Task<List<ClassOffering>> ListPublishedAsync(string? slug, DateTimeOffset? endingAfter, CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the development seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the seed offerings Business chose, with their SKUs and one audit entry, in one save.</summary>
    Task<int> AddSeedAsync(
        IReadOnlyList<ClassOffering> offerings, SkuChanges skus, string actor, CancellationToken cancellationToken);
}
