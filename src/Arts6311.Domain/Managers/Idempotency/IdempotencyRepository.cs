using Arts6311.Domain.Managers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Managers.Idempotency;

internal interface IIdempotencyRepository
{
    Task DeleteExpiredAsync(IdempotencyScope scope, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts and saves the record. Throws <see cref="DbUpdateException"/> when the save fails — the scope's unique
    /// index among other reasons; the caller tells a lost race from anything else by looking for the record.
    /// </summary>
    Task InsertAsync(IdempotencyRecord record, CancellationToken cancellationToken);

    Task<StoredIdempotentResult?> FindAsync(IdempotencyScope scope, CancellationToken cancellationToken);

    /// <summary>Writes the operation's result onto the claimed record, tracked or not.</summary>
    Task SaveResultAsync(IdempotencyRecord record, string resultJson, CancellationToken cancellationToken);

    /// <summary>One set-based DELETE of at most <paramref name="batchSize"/> expired rows; returns how many went.</summary>
    Task<int> DeleteExpiredBatchAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken);
}

internal sealed class IdempotencyRepository(ArtsDbContext context) : IIdempotencyRepository
{
    public async Task DeleteExpiredAsync(IdempotencyScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The scope is unique, so this is a single indexed row. Expiry is compared in code: it is one row, and the
        // code path then needs no provider to translate a DateTimeOffset comparison.
        var record = await InScope(scope).SingleOrDefaultAsync(cancellationToken);
        if (record is not null && record.ExpiresAt <= now)
        {
            context.IdempotencyRecords.Remove(record);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public Task InsertAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        context.IdempotencyRecords.Add(record);
        return context.SaveChangesAsync(cancellationToken);
    }

    public Task<StoredIdempotentResult?> FindAsync(IdempotencyScope scope, CancellationToken cancellationToken) =>
        InScope(scope).AsNoTracking()
            .Select(record => new StoredIdempotentResult(record.FingerprintHash, record.ResultJson))
            .SingleOrDefaultAsync(cancellationToken);

    public Task SaveResultAsync(IdempotencyRecord record, string resultJson, CancellationToken cancellationToken)
    {
        // An operation that lost a race may clear the change tracker and still succeed, answering with the winner's
        // result; that detaches the claimed record too. Re-attached, the result is written — otherwise the
        // placeholder would commit and every replay of the key would answer with an empty body.
        if (context.Entry(record).State == EntityState.Detached)
        {
            context.IdempotencyRecords.Attach(record);
        }

        record.ResultJson = resultJson;
        context.Entry(record).Property(row => row.ResultJson).IsModified = true;

        return context.SaveChangesAsync(cancellationToken);
    }

    // Set-based and bounded, so a large backlog never loads into memory and no DELETE holds locks for long. Served by
    // IX_IdempotencyRecords_ExpiresAt. Bypasses the change tracker, which suits a sweep that tracks nothing.
    public Task<int> DeleteExpiredBatchAsync(DateTimeOffset now, int batchSize, CancellationToken cancellationToken) =>
        context.IdempotencyRecords
            .Where(record => record.ExpiresAt <= now)
            .OrderBy(record => record.ExpiresAt)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);

    private IQueryable<IdempotencyRecord> InScope(IdempotencyScope scope) =>
        context.IdempotencyRecords.Where(record =>
            record.Subject == scope.Subject
            && record.Operation == scope.Operation
            && record.Key == scope.Key);
}
