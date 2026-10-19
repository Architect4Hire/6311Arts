using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Managers.Persistence;

/// <summary>
/// The one application DbContext: one assembly, one migration history (backend.md). Every module and the
/// shared kernel contribute <see cref="IEntityTypeConfiguration{TEntity}"/> classes; nothing configures the
/// model inline here.
/// </summary>
/// <remarks>
/// <para>
/// Registered pooled through the Aspire SQL Server EF integration (keyed <see cref="ConnectionName"/>), so it
/// takes no constructor dependency beyond its options. Interceptors are added once, by
/// <see cref="ArtsDbContextOptions.Configure"/>.
/// </para>
/// <para>
/// It derives from <see cref="IdentityDbContext{TUser,TRole,TKey}"/> so ASP.NET Core Identity's stores share this
/// one context and one migration history (ADR 0002, ADR 0023). Naming a module's entity type here is the single
/// documented exception to "the shared kernel never depends on a module" (backend.md), and
/// <c>ModuleBoundaryScanner.KernelExemptions</c> lists this exact file.
/// </para>
/// </remarks>
public sealed class ArtsDbContext(DbContextOptions<ArtsDbContext> options)
    : IdentityDbContext<ArtsUser, ArtsRole, string>(options)
{
    /// <summary>The Aspire resource and connection-string name.</summary>
    public const string ConnectionName = "appdb";

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<EmailDelivery> EmailDeliveries => Set<EmailDelivery>();

    // Named `builder` to match IdentityDbContext's override, not EF's usual `modelBuilder`.
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Identity's own model first, so our configurations below refine it rather than being overwritten by it.
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ArtsDbContext).Assembly);
    }
}
