using Arts6311.Domain.Managers.Audit;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Managers.Persistence;

/// <summary>
/// The options every host applies to <see cref="ArtsDbContext"/>, passed as the Aspire integration's
/// <c>configureDbContextOptions</c> callback. Keeping it here means the API, the Worker and the migration
/// service can never disagree about which interceptors run.
/// </summary>
public static class ArtsDbContextOptions
{
    public static void Configure(DbContextOptionsBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Stateless, so one instance serves every pooled context.
        options.AddInterceptors(AuditLogImmutabilityInterceptor.Instance);
    }
}
