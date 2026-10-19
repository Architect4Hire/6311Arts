using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Identity.Data.Repositories;

internal sealed class IdentityRepository(ArtsDbContext context) : IIdentityRepository
{
    /// <summary>
    /// Looked up by the normalized email, which is the column Identity keeps indexed and unique. Tracked, because
    /// the caller may hand the entity to <c>UserManager</c> to update.
    /// </summary>
    public Task<ArtsUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        context.Users.FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<ArtsUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        context.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
