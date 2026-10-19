using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Managers.Idempotency;

internal interface IIdempotencyDataLayer
{
    /// <summary>
    /// Runs <paramref name="operation"/> and records its successful result in one transaction on the request's
    /// DbContext, so the operation's own writes — its outbox messages included — and the idempotency record commit
    /// together or not at all.
    /// </summary>
    Task<IdempotencyAttempt<T>> ExecuteAsync<T>(
        IdempotencyScope scope,
        byte[] fingerprintHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        Func<CancellationToken, Task<OperationResult<T>>> operation,
        Func<T, string> serializeResult,
        CancellationToken cancellationToken);

    /// <summary>Deletes up to <paramref name="batchSize"/> records that expired by <paramref name="now"/>; returns how many.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);
}

internal sealed class IdempotencyDataLayer(ArtsDbContext context, IIdempotencyRepository records) : IIdempotencyDataLayer
{
    private const int MaxAttempts = 3;

    private static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(50);

    public Task<IdempotencyAttempt<T>> ExecuteAsync<T>(
        IdempotencyScope scope,
        byte[] fingerprintHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        Func<CancellationToken, Task<OperationResult<T>>> operation,
        Func<T, string> serializeResult,
        CancellationToken cancellationToken)
    {
        // The one explicit transaction backend.md allows for, run through the execution strategy as a callback:
        // the Aspire SQL Server integration retries transient failures, and a retrying strategy re-runs the whole
        // unit, so each attempt starts from a clean tracker. The wrapped operation may therefore run more than once
        // on a transient fault, and must be safe to repeat (it rolled back with the failed attempt).
        return context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            for (var attempt = 1; ; attempt++)
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

                await records.DeleteExpiredAsync(scope, now, cancellationToken);

                var record = new IdempotencyRecord
                {
                    Subject = scope.Subject,
                    Operation = scope.Operation,
                    Key = scope.Key,
                    FingerprintHash = fingerprintHash,
                    ResultJson = "{}", // placeholder until the operation succeeds; never committed as such
                    CreatedAt = now,
                    ExpiresAt = expiresAt,
                };

                // Claims the scope. A concurrent request with the same scope blocks here until this commits.
                try
                {
                    await records.InsertAsync(record, cancellationToken);
                }
                catch (DbUpdateException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear();

                    // A record for the scope now exists: this request lost the race, and answers with the winner's
                    // result. Decided by what the database holds, not by which error it raised, so no provider's
                    // error numbers are needed here.
                    var existing = await records.FindAsync(scope, cancellationToken);
                    if (existing is not null)
                    {
                        return IdempotencyAttempt<T>.Found(existing);
                    }

                    // No record, so not (or not yet visibly) a lost race: the conflicting record expired and was
                    // removed in between, or the insert failed for another reason — a deadlock, a timeout, a lock the
                    // winner still holds. Brief backoff, then try again.
                    if (attempt < MaxAttempts)
                    {
                        await Task.Delay(RetryBackoff * attempt, cancellationToken);
                        continue;
                    }

                    // Still nothing: surface the real failure, so the execution strategy can retry it if it is transient
                    // (a deadlock is) instead of a generic error it would never recognise.
                    throw;
                }

                var result = await operation(cancellationToken);
                if (!result.Succeeded)
                {
                    // Nothing is remembered for failures: the business writes roll back and a retry runs again.
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                    return IdempotencyAttempt<T>.Ran(result);
                }

                await records.SaveResultAsync(record, serializeResult(result.Value!), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return IdempotencyAttempt<T>.Ran(result);
            }
        });
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
        records.DeleteExpiredBatchAsync(now, batchSize, cancellationToken);
}
