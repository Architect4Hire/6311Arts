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

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
