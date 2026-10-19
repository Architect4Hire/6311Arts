using Arts6311.Domain.Managers.Persistence;
using Arts6311.MigrationService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arts6311.Tests.MigrationService;

public sealed class MigrationWorkerTests
{
    [Fact]
    public async Task MigrationsRunBeforeSeedersAndTheRunExitsZero()
    {
        var log = new List<string>();
        var (worker, state, lifetime) = Build(new RecordingTarget("appdb", log), new RecordingSeeder("admin", log));

        await RunAsync(worker);

        Assert.Equal(["migrate:appdb", "seed:admin"], log);
        Assert.Equal(0, state.ExitCode);
        Assert.True(lifetime.Stopped);
    }

    [Fact]
    public async Task AFailedMigrationSkipsSeedersAndExitsNonZero()
    {
        var log = new List<string>();
        var (worker, state, lifetime) = Build(new RecordingTarget("appdb", log, fail: true), new RecordingSeeder("admin", log));

        await RunAsync(worker);

        Assert.Equal(["migrate:appdb"], log);
        Assert.Equal(1, state.ExitCode);
        Assert.True(lifetime.Stopped);
    }

    [Fact]
    public void ARunThatNeverHappenedIsNotASuccess()
    {
        Assert.Equal(1, new MigrationRunState().ExitCode);
    }

    private static async Task RunAsync(MigrationWorker worker)
    {
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await worker.ExecuteTask!;
    }

    private static (MigrationWorker Worker, MigrationRunState State, StubLifetime Lifetime) Build(
        IMigrationTarget target, IDataSeeder seeder)
    {
        var services = new ServiceCollection()
            .AddScoped(_ => target)
            .AddScoped(_ => seeder)
            .BuildServiceProvider();
        var state = new MigrationRunState();
        var lifetime = new StubLifetime();
        var environment = new StubEnvironment();
        return (new MigrationWorker(services, environment, lifetime, state, NullLogger<MigrationWorker>.Instance), state, lifetime);
    }

    private sealed class RecordingTarget(string name, List<string> log, bool fail = false) : IMigrationTarget
    {
        public string Name => name;

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            log.Add($"migrate:{name}");
            return fail ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        }
    }

    private sealed class RecordingSeeder(string name, List<string> log) : IDataSeeder
    {
        public string Name => name;

        public Task SeedAsync(CancellationToken cancellationToken)
        {
            log.Add($"seed:{name}");
            return Task.CompletedTask;
        }
    }

    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public bool Stopped { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => Stopped = true;
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Arts6311.MigrationService.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
