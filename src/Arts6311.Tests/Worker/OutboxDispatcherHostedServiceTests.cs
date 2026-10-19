using System.Diagnostics;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Arts6311.Tests.Worker;

public sealed class OutboxDispatcherHostedServiceTests : IAsyncDisposable
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero));
    private readonly DispatcherScript script = new();
    private readonly CapturingLogger logger = new();
    private readonly ServiceProvider services;
    private readonly OutboxDispatcherHostedService service;

    public OutboxDispatcherHostedServiceTests()
    {
        services = new ServiceCollection()
            .AddSingleton(script)
            .AddScoped<IOutboxDispatcher, ScriptedDispatcher>()
            .BuildServiceProvider(validateScopes: true);
        service = new OutboxDispatcherHostedService(services.GetRequiredService<IServiceScopeFactory>(), time, logger);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        service.Dispose();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task EachPassGetsAFreshScope()
    {
        await service.DispatchOnceAsync(Ct);
        await service.DispatchOnceAsync(Ct);

        Assert.Equal(2, script.Dispatchers.Count);
        Assert.NotSame(script.Dispatchers[0], script.Dispatchers[1]);
    }

    [Fact]
    public async Task ASummaryIsLoggedOnlyWhenThePassClaimedWork()
    {
        script.Next = _ => new OutboxDispatchSummary(0, 0, 0, 0);
        await service.DispatchOnceAsync(Ct);
        Assert.Empty(logger.Entries);

        script.Next = _ => new OutboxDispatchSummary(3, 1, 1, 1);
        await service.DispatchOnceAsync(Ct);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Outbox pass claimed 3: 1 completed, 1 retrying, 1 poisoned.", entry.Message);
    }

    [Fact]
    public async Task AFailedPassIsLoggedAndDoesNotStopTheService()
    {
        script.Next = _ => throw new InvalidOperationException("SQL is down.");

        var summary = await service.DispatchOnceAsync(Ct);

        Assert.Null(summary);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task ATimeoutInsideAPassIsAFailureNotAShutdown()
    {
        script.Next = _ => throw new TaskCanceledException("Command timeout.");

        Assert.Null(await service.DispatchOnceAsync(Ct));
        Assert.Equal(LogLevel.Error, Assert.Single(logger.Entries).Level);
    }

    [Fact]
    public async Task ShutdownDuringAPassPropagates()
    {
        using var shutdown = new CancellationTokenSource();
        script.Next = token =>
        {
            shutdown.Cancel();
            token.ThrowIfCancellationRequested();
            return new OutboxDispatchSummary(0, 0, 0, 0);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DispatchOnceAsync(shutdown.Token));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task AFailureCausedByShutdownIsNotLoggedAsAnError()
    {
        using var shutdown = new CancellationTokenSource();
        script.Next = _ =>
        {
            shutdown.Cancel();
            throw new InvalidOperationException("Stand-in for a SqlException from a cancelled command.");
        };

        Assert.Null(await service.DispatchOnceAsync(shutdown.Token));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Debug, entry.Level);
        Assert.Equal("Outbox pass interrupted by shutdown.", entry.Message);
    }

    [Fact]
    public async Task ItPassesOnceAtStartupThenOncePerPollingInterval()
    {
        await service.StartAsync(Ct);
        await script.WaitForCallsAsync(1);

        time.Advance(OutboxPolicy.PollingInterval - TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, script.Calls);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await script.WaitForCallsAsync(2);

        time.Advance(OutboxPolicy.PollingInterval);
        await script.WaitForCallsAsync(3);

        await service.StopAsync(Ct);
        Assert.NotNull(service.ExecuteTask);
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
    }

    /// <summary>
    /// The noise fix, under the OpenTelemetry SDK's default (parent-based) sampler — what the Worker runs with. A span
    /// started anywhere inside a pass (the SQL the read issues) is not recorded; the same span outside a pass is.
    /// </summary>
    [Fact]
    public async Task SpansStartedInsideAPassAreNotRecorded()
    {
        using var source = new ActivitySource($"test.sql.{Guid.NewGuid():N}");
        var exported = new List<Activity>();
        using var tracing = Sdk.CreateTracerProviderBuilder()
            .AddSource(source.Name)
            .AddProcessor(new CapturingProcessor(exported))
            .Build();

        // Observed inside the pass, asserted outside it: the service would swallow an assertion thrown in a pass.
        string? currentInsidePass = null;
        Activity? insidePass = null;
        script.Next = _ =>
        {
            currentInsidePass = Activity.Current?.OperationName;
            insidePass = source.StartActivity("SELECT OutboxMessages");
            insidePass?.Dispose();
            return new OutboxDispatchSummary(0, 0, 0, 0);
        };

        Activity.Current = null;
        Assert.NotNull(await service.DispatchOnceAsync(Ct));
        Assert.Equal(OutboxDispatcherHostedService.PollActivityName, currentInsidePass);
        using (var outsidePass = source.StartActivity("SELECT elsewhere"))
        {
            Assert.NotNull(outsidePass);
            Assert.True(outsidePass.Recorded);
        }

        Assert.False(insidePass?.Recorded ?? false);
        Assert.Equal(["SELECT elsewhere"], exported.Select(activity => activity.DisplayName));
    }

    /// <summary>Registered as a DI singleton, so the provider disposes it.</summary>
    private sealed class DispatcherScript : IDisposable
    {
        private readonly SemaphoreSlim called = new(0);
        private int calls;

        public Func<CancellationToken, OutboxDispatchSummary> Next { get; set; } = _ => new OutboxDispatchSummary(0, 0, 0, 0);

        public List<ScriptedDispatcher> Dispatchers { get; } = [];

        public int Calls => Volatile.Read(ref calls);

        public OutboxDispatchSummary Run(ScriptedDispatcher dispatcher, CancellationToken cancellationToken)
        {
            lock (Dispatchers)
            {
                Dispatchers.Add(dispatcher);
            }

            try
            {
                return Next(cancellationToken);
            }
            finally
            {
                Interlocked.Increment(ref calls);
                called.Release();
            }
        }

        public async Task WaitForCallsAsync(int expected)
        {
            while (Calls < expected)
            {
                Assert.True(await called.WaitAsync(TimeSpan.FromSeconds(10), Ct), $"Expected pass {expected} never ran.");
            }
        }

        public void Dispose() => called.Dispose();
    }

    private sealed class ScriptedDispatcher(DispatcherScript script) : IOutboxDispatcher
    {
        public Task<OutboxDispatchSummary> DispatchDueAsync(CancellationToken cancellationToken) =>
            Task.FromResult(script.Run(this, cancellationToken));
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger<OutboxDispatcherHostedService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    private sealed class CapturingProcessor(List<Activity> exported) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            lock (exported)
            {
                exported.Add(data);
            }
        }
    }
}
