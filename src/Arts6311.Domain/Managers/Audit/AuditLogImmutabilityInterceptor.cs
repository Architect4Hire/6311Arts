using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Arts6311.Domain.Managers.Audit;

/// <summary>
/// Rejects a <em>tracked</em> update or delete of an <see cref="AuditLog"/> row at <c>SaveChanges</c>. Added to
/// every <c>ArtsDbContext</c> by <c>ArtsDbContextOptions.Configure</c>.
/// </summary>
/// <remarks>
/// Application-enforced only: <c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, raw SQL and other connections never pass
/// through here, and the database has no guard yet (ADR 0017, DECIDE). There is no retention or erasure path
/// either; one would need its own ADR.
/// </remarks>
internal sealed class AuditLogImmutabilityInterceptor : SaveChangesInterceptor
{
    public static readonly AuditLogImmutabilityInterceptor Instance = new();

    private AuditLogImmutabilityInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        RejectMutation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        RejectMutation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void RejectMutation(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    $"An AuditLog row cannot be {(entry.State == EntityState.Deleted ? "deleted" : "modified")}; it is immutable once written.");
            }
        }
    }
}
