using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Pages.Data;

/// <summary>Where a list page resumes: after this row in (Title, Id) order.</summary>
internal sealed record PageListPosition(string Title, Guid Id);

internal enum PageWriteOutcome
{
    Saved = 0,

    /// <summary>Someone else saved first: the row version moved under this write.</summary>
    Conflict = 10,

    /// <summary>Another page took the slug between Business's check and the save.</summary>
    SlugTaken = 20,
}

internal interface IPagesDataLayer
{
    /// <summary>One page with its image references, tracked.</summary>
    Task<Page?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, Guid? exceptId, CancellationToken cancellationToken);

    Task<List<PageSummaryServiceModel>> ListAsync(
        PageListPosition? after, int take, PublishStatus? status, string? search, CancellationToken cancellationToken);

    /// <summary>Adds the page Business built, image references included, with its bookkeeping and audit entry, in one save.</summary>
    Task<PageWriteOutcome> CreateAsync(Page page, string actorUserId, string summary, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a staff edit already applied to <paramref name="page"/>, with its audit entry, in one save — and, when
    /// <paramref name="pathChange"/> is set, <c>redirects.slug-changed</c> staged before it (ADR 0035).
    /// </summary>
    Task<PageWriteOutcome> UpdateAsync(
        Page page, string actorUserId, string summary, PublishedPathChange? pathChange, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a status change Business already applied, with its audit entry, in one save. From 2.8 this is also where
    /// <c>site.revalidate-requested</c> is staged (ADR 0031, 0034).
    /// </summary>
    Task<PageWriteOutcome> TransitionAsync(
        Page page, PublishCommand command, PublishStatus from, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Every slug in use, for the placeholder seed.</summary>
    Task<HashSet<string>> GetSlugsAsync(CancellationToken cancellationToken);

    /// <summary>Adds the placeholder pages Business chose, with one audit entry, in one save. Returns how many.</summary>
    Task<int> AddSeedAsync(IReadOnlyList<Page> pages, string actor, CancellationToken cancellationToken);
}
