using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Worker;

public sealed class IdempotencyCleanupHostedServiceTests : IAsyncDisposable
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly ScriptedMaintenance maintenance = new();
    private readonly ServiceProvider services;

    public IdempotencyCleanupHostedServiceTests() =>
        services = new ServiceCollection()

            // An instance registration, which no scope ever disposes. A scoped factory returning the fake made each
            // sweep's scope dispose it — semaphore and all — so the next wait threw ObjectDisposedException.
            .AddSingleton<IIdempotencyMaintenance>(maintenance)
            .BuildServiceProvider(validateScopes: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        maintenance.Dispose();
    }

    [Fact]
    public async Task ASweepRepeatsFullBatchesUntilOneComesBackShort()
    {
        maintenance.Batches.Enqueue(IdempotencyPolicy.SweepBatchSize);
        maintenance.Batches.Enqueue(IdempotencyPolicy.SweepBatchSize);
        maintenance.Batches.Enqueue(7);
        maintenance.Batches.Enqueue(IdempotencyPolicy.SweepBatchSize); // never reached
        using var service = Service(out var logger);

        var deleted = await service.SweepOnceAsync(Ct);

        Assert.Equal((2 * IdempotencyPolicy.SweepBatchSize) + 7, deleted);
        Assert.Equal(3, maintenance.Calls);
        Assert.Equal(LogLevel.Information, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task AnEmptySweepLogsNothing()
    {
        using var service = Service(out var logger);

        Assert.Equal(0, await service.SweepOnceAsync(Ct));
        Assert.Empty(logger.Levels);
    }

    [Fact]
    public async Task AFailedSweepIsLoggedAndTheServiceCarriesOn()
    {
        maintenance.Throw = new InvalidOperationException("SQL is down.");
        using var service = Service(out var logger);

        Assert.Null(await service.SweepOnceAsync(Ct));
        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task ShutdownStopsTheSweepBetweenBatches()
    {
        using var shutdown = new CancellationTokenSource();
        maintenance.Batches.Enqueue(IdempotencyPolicy.SweepBatchSize);
        maintenance.Batches.Enqueue(IdempotencyPolicy.SweepBatchSize);
        maintenance.AfterCall = () => shutdown.Cancel();
        using var service = Service(out _);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SweepOnceAsync(shutdown.Token));
        Assert.Equal(1, maintenance.Calls);
    }

    [Fact]
    public async Task ItSweepsAtStartupThenOncePerInterval()
    {
        using var service = Service(out _);

        await service.StartAsync(Ct);
        await maintenance.WaitForCallsAsync(1);
        time.Advance(IdempotencyPolicy.SweepInterval);
        await maintenance.WaitForCallsAsync(2);
        await service.StopAsync(Ct);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    private IdempotencyCleanupHostedService Service(out LevelLogger logger)
    {
        logger = new LevelLogger();
        return new IdempotencyCleanupHostedService(services.GetRequiredService<IServiceScopeFactory>(), time, logger);
    }

    /// <remarks>Owned by the test class, which disposes it; the container only hands the instance out.</remarks>
    private sealed class ScriptedMaintenance : IIdempotencyMaintenance, IDisposable
    {
        private readonly SemaphoreSlim called = new(0);
        private int calls;

        public Queue<int> Batches { get; } = new();

        public Exception? Throw { get; set; }

        public Action? AfterCall { get; set; }

        public int Calls => Volatile.Read(ref calls);

        public Task<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (Throw is not null)
                {
                    throw Throw;
                }

                return Task.FromResult(Batches.TryDequeue(out var batch) ? batch : 0);
            }
            finally
            {
                Interlocked.Increment(ref calls);
                AfterCall?.Invoke();
                called.Release();
            }
        }

        public async Task WaitForCallsAsync(int expected)
        {
            while (Calls < expected)
            {
                Assert.True(await called.WaitAsync(TimeSpan.FromSeconds(10), Ct), $"Expected sweep call {expected} never came.");
            }
        }

        public void Dispose() => called.Dispose();
    }

    private sealed class LevelLogger : ILogger<IdempotencyCleanupHostedService>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }
}
