using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Kernel;

public sealed class IdempotencySweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OnlyRecordsAlreadyExpiredAreDeleted()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await SeedAsync(database, ("expired", Now - TimeSpan.FromSeconds(1)), ("expires-now", Now), ("live", Now + TimeSpan.FromSeconds(1)));

        await using var context = database.NewContext();
        var deleted = await Maintenance(context).DeleteExpiredBatchAsync(Ct);

        Assert.Equal(2, deleted);
        await using var reader = database.NewContext();
        Assert.Equal(["live"], await reader.IdempotencyRecords.Select(record => record.Key).ToListAsync(Ct));
    }

    [Fact]
    public async Task OneBatchDeletesAtMostTheBatchSizeOldestFirst()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        await SeedAsync(database, [.. Enumerable.Range(0, IdempotencyPolicy.SweepBatchSize + 2)
            .Select(index => ($"k{index}", Now - TimeSpan.FromMinutes(index + 1)))]);

        await using var context = database.NewContext();
        Assert.Equal(IdempotencyPolicy.SweepBatchSize, await Maintenance(context).DeleteExpiredBatchAsync(Ct));

        // The two left are the two most recently expired: k0 and k1.
        await using (var reader = database.NewContext())
        {
            Assert.Equal(["k0", "k1"], await reader.IdempotencyRecords.Select(record => record.Key).OrderBy(key => key).ToListAsync(Ct));
        }

        Assert.Equal(2, await Maintenance(context).DeleteExpiredBatchAsync(Ct));
        Assert.Equal(0, await Maintenance(context).DeleteExpiredBatchAsync(Ct));
    }

    private static IdempotencyMaintenance Maintenance(Domain.Managers.Persistence.ArtsDbContext context) =>
        new(new IdempotencyDataLayer(context, new IdempotencyRepository(context)), new SystemClock(new FakeTimeProvider(Now)));

    private static async Task SeedAsync(SqliteArtsDatabase database, params (string Key, DateTimeOffset ExpiresAt)[] records)
    {
        await using var context = database.NewContext();
        context.IdempotencyRecords.AddRange(records.Select(record => new IdempotencyRecord
        {
            Subject = "user:1",
            Operation = "test.op",
            Key = record.Key,
            FingerprintHash = new byte[32],
            ResultJson = "{}",
            CreatedAt = record.ExpiresAt - IdempotencyPolicy.Retention,
            ExpiresAt = record.ExpiresAt,
        }));
        await context.SaveChangesAsync(Ct);
    }
}
