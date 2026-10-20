using Arts6311.Domain.Modules.Identity.Data.Entities;

namespace Arts6311.Domain.Modules.Identity.Data.Repositories;

/// <summary>
/// EF queries and the save for this module. Identity's own writes go through <c>UserManager</c>, which owns its
/// persistence; this covers the reads that would be awkward through the manager, and the one save that commits
/// our audit rows and outbox messages together.
/// </summary>
internal interface IIdentityRepository
{
    Task<ArtsUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<ArtsUser?> FindByIdAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Throws away this context's unsaved changes to a user and reads the row again. For a write Identity's store
    /// refused, which leaves the entity modified in the tracker: the next save — our audit row's — would otherwise
    /// try to write it again and fail the same way.
    /// </summary>
    Task ReloadAsync(ArtsUser user, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
