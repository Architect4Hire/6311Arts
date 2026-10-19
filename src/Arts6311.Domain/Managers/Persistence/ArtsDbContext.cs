using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Managers.Persistence;

/// <summary>
/// The one application DbContext: one assembly, one migration history (backend.md). Every module and the
/// shared kernel contribute <see cref="IEntityTypeConfiguration{TEntity}"/> classes; nothing configures the
/// model inline here.
/// </summary>
/// <remarks>
/// Registered pooled through the Aspire SQL Server EF integration (keyed <see cref="ConnectionName"/>), so it
/// takes no constructor dependency beyond its options. Interceptors are added once, by
/// <see cref="ArtsDbContextOptions.Configure"/>.
/// </remarks>
public sealed class ArtsDbContext(DbContextOptions<ArtsDbContext> options) : DbContext(options)
{
    /// <summary>The Aspire resource and connection-string name.</summary>
    public const string ConnectionName = "appdb";

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ArtsDbContext).Assembly);
    }
}
