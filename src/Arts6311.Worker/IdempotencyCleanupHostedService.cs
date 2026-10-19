using System.Diagnostics;
using Arts6311.Domain.Managers.Idempotency;

namespace Arts6311.Worker;

/// <summary>
/// Deletes expired idempotency records: one sweep at startup, then one every <see cref="IdempotencyPolicy.SweepInterval"/>,
/// each in a fresh DI scope, in batches of <see cref="IdempotencyPolicy.SweepBatchSize"/> until a batch comes back short.
/// </summary>
/// <remarks>
/// <para>
/// No claim is needed to run on several replicas (add-background-job step 2): each batch is one set-based DELETE of
/// rows that are already dead, so two replicas sweeping together delete each row once between them, and a lost race
/// only means a smaller count. Expired records are also removed lazily when their scope is next used, so a missed
/// sweep costs only table size.
/// </para>
/// <para>
/// Cancellation is honoured between batches; a batch is one statement, so nothing is ever half-applied.
/// </para>
/// </remarks>
internal sealed partial class IdempotencyCleanupHostedService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<IdempotencyCleanupHostedService> logger) : BackgroundService
{
    internal const string SweepActivityName = "idempotency.sweep";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(IdempotencyPolicy.SweepInterval, timeProvider);

        try
        {
            do
            {
                await SweepOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown between or during batches; whatever is left is deleted by the next sweep.
        }
    }

    /// <returns>Rows deleted, or <c>null</c> when the sweep failed (logged; the next tick tries again).</returns>
    /// <remarks>
    /// Shutdown: cancellation of <paramref name="stoppingToken"/> propagates as <see cref="OperationCanceledException"/>
    /// (ending <see cref="ExecuteAsync"/>); any other exception raised once shutdown has begun is logged at Debug and
    /// returns <c>null</c>, because a cancelled SQL command can surface as a <c>SqlException</c>.
    /// </remarks>
    internal async Task<int?> SweepOnceAsync(CancellationToken stoppingToken)
    {
        // A root span of its own: one per hour, carrying the count.
        using var activity = WorkerDiagnostics.Source.StartActivity(SweepActivityName);
        var deleted = 0;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IIdempotencyMaintenance>();

            int batch;
            do
            {
                stoppingToken.ThrowIfCancellationRequested();
                batch = await maintenance.DeleteExpiredBatchAsync(stoppingToken);
                deleted += batch;
            }
            while (batch == IdempotencyPolicy.SweepBatchSize);

            activity?.SetTag("idempotency.deleted", deleted);
            if (deleted > 0)
            {
                LogSwept(deleted);
            }

            return deleted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            activity?.SetTag("idempotency.deleted", deleted);

            if (stoppingToken.IsCancellationRequested)
            {
                LogSweepInterrupted();
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                LogSweepFailed(ex);
            }

            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotency sweep deleted {Deleted} expired records.")]
    private partial void LogSwept(int deleted);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Idempotency sweep interrupted by shutdown.")]
    private partial void LogSweepInterrupted();

    [LoggerMessage(Level = LogLevel.Error, Message = "Idempotency sweep failed; retrying on the next tick.")]
    private partial void LogSweepFailed(Exception exception);
}
