using Arts6311.Domain.Managers.Time;

namespace Arts6311.Domain.Managers.Idempotency;

/// <summary>
/// The idempotency kernel's housekeeping boundary, for the Worker's sweep (add-background-job: a job works through a
/// facade, never the DbContext). Records also expire lazily — the executor deletes an expired record in its scope
/// before claiming — so the sweep only keeps the table from growing.
/// </summary>
public interface IIdempotencyMaintenance
{
    /// <summary>Deletes one batch of records expired as of now; returns how many were deleted.</summary>
    Task<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken);
}

internal sealed class IdempotencyMaintenance(IIdempotencyDataLayer dataLayer, IClock clock) : IIdempotencyMaintenance
{
    public Task<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken) =>
        dataLayer.DeleteExpiredAsync(clock.UtcNow, IdempotencyPolicy.SweepBatchSize, cancellationToken);
}
