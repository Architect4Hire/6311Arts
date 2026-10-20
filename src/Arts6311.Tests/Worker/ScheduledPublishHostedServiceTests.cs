using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Revalidation;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Tests.Kernel;
using Arts6311.Tests.Media.Infrastructure;
using Arts6311.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Worker;

/// <summary>
/// The scheduled publisher (ADR 0036): what a pass counts, how it behaves at shutdown and on failure, and — on a
/// real database, through the Artists module — that two Workers racing publish one artist once.
/// </summary>
public sealed class ScheduledPublishHostedServiceTests
{
    private const string Editor = "editor-1";

    private static readonly DateTimeOffset Start = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APassCountsEveryOutcomeAcrossEveryContentType()
    {
        var artists = new ScriptedPublisher("artist", ScheduledPublishOutcome.Published, ScheduledPublishOutcome.Lost);
        var pages = new ScriptedPublisher("page", ScheduledPublishOutcome.Unscheduled, ScheduledPublishOutcome.Skipped);
        await using var services = Scripted(artists, pages);
        using var service = Service(services, out var logger);

        var summary = await service.PublishDueAsync(Ct);

        Assert.Equal(new ScheduledPublishSummary(Due: 4, Published: 1, Unscheduled: 1, Lost: 1, Skipped: 1), summary);
        Assert.Equal(Start, artists.AsOf);

        // Every publisher is asked, and each is asked once per pass.
        Assert.Equal(1, artists.Passes);
        Assert.Equal(1, pages.Passes);
    }

    [Fact]
    public async Task AUnitThatThrowsIsCountedAndTheOnesBehindItStillRun()
    {
        // The due list is ordered by time, so without this a record that always fails would be first every minute
        // and nothing behind it would ever be published (ADR 0036).
        var publisher = new ScriptedPublisher("artist", ScheduledPublishOutcome.Published, ScheduledPublishOutcome.Published)
        {
            ThrowOnUnit = 1,
        };
        await using var services = Scripted(publisher);
        using var service = Service(services, out var logger);

        var summary = await service.PublishDueAsync(Ct);

        Assert.Equal(new ScheduledPublishSummary(Due: 2, Published: 1, Unscheduled: 0, Lost: 0, Skipped: 0, Failed: 1), summary);
        Assert.Equal(2, publisher.Units);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task EveryUnitRunsInAScopeOfItsOwn()
    {
        // So a unit that throws after staging entities cannot leave them tracked for the next unit's save to
        // re-attempt and fail on (ADR 0036).
        var publisher = new ScriptedPublisher(
            "artist", ScheduledPublishOutcome.Published, ScheduledPublishOutcome.Published);
        await using var services = Scripted(publisher);
        using var service = Service(services, out _);

        await service.PublishDueAsync(Ct);

        Assert.Equal(2, publisher.UnitScopes.Count);
        Assert.Equal(2, publisher.UnitScopes.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, publisher.UnitScopes);
    }

    [Fact]
    public async Task AnEmptyPassSaysNothing()
    {
        await using var services = Scripted(new ScriptedPublisher("artist"));
        using var service = Service(services, out var logger);

        Assert.Equal(new ScheduledPublishSummary(0, 0, 0, 0, 0), await service.PublishDueAsync(Ct));
        Assert.Empty(logger.Levels);
    }

    [Fact]
    public async Task AFailedPassIsLoggedAndTheServiceCarriesOn()
    {
        var publisher = new ScriptedPublisher("artist") { Throw = new InvalidOperationException("SQL is down.") };
        await using var services = Scripted(publisher);
        using var service = Service(services, out var logger);

        Assert.Null(await service.PublishDueAsync(Ct));
        Assert.Equal(LogLevel.Error, Assert.Single(logger.Levels));
    }

    [Fact]
    public async Task ShutdownStopsThePassBetweenUnits()
    {
        using var shutdown = new CancellationTokenSource();
        var publisher = new ScriptedPublisher(
            "artist", ScheduledPublishOutcome.Published, ScheduledPublishOutcome.Published);
        publisher.AfterUnit = shutdown.Cancel;
        await using var services = Scripted(publisher);
        using var service = Service(services, out _);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PublishDueAsync(shutdown.Token));

        // The first unit finished and committed; the second was never started, so nothing is half-applied.
        Assert.Equal(1, publisher.Units);
    }

    [Fact]
    public async Task ItRunsAtStartupThenOncePerInterval()
    {
        var publisher = new ScriptedPublisher("artist");
        await using var services = Scripted(publisher);
        var time = new FakeTimeProvider(Start);
        using var service = new ScheduledPublishHostedService(
            services.GetRequiredService<IServiceScopeFactory>(), time, NullLogger<ScheduledPublishHostedService>.Instance);

        await service.StartAsync(Ct);
        await publisher.WaitForPassesAsync(1);
        time.Advance(ScheduledPublishPolicy.Interval);
        await publisher.WaitForPassesAsync(2);
        await service.StopAsync(Ct);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
    }

    /// <summary>
    /// The guard a second Worker replica relies on, through the real Artists module: B runs its whole pass between
    /// A reading the artist as due and A's save reaching the database. A's save names the row version it read,
    /// matches nothing, and A reports that it lost (ADR 0036).
    /// </summary>
    [Fact]
    public async Task TwoWorkersRacingPublishAScheduledArtistOnce()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var time = new FakeTimeProvider(Start);
        await using var second = Module(database, time);
        var artistId = await ScheduleAnArtistAsync(second, time);
        time.Advance(TimeSpan.FromMinutes(2));

        ScheduledPublishSummary? secondSummary = null;
        var raceInsideTheFirstSave = new BeforeFirstSave(async () =>
            secondSummary = await Service(second, out _).PublishDueAsync(Ct));
        await using var first = Module(database, time, raceInsideTheFirstSave);

        var firstSummary = await Service(first, out _).PublishDueAsync(Ct);

        Assert.Equal(new ScheduledPublishSummary(Due: 1, Published: 1, Unscheduled: 0, Lost: 0, Skipped: 0), secondSummary);
        Assert.Equal(new ScheduledPublishSummary(Due: 1, Published: 0, Unscheduled: 0, Lost: 1, Skipped: 0), firstSummary);

        await using var context = database.NewContext();
        var artist = await context.Set<Artist>().AsNoTracking().SingleAsync(row => row.Id == artistId, Ct);
        Assert.Equal(PublishStatus.Published, artist.Status);
        Assert.Null(artist.ScheduledFor);

        // Published once: one audit row, and one revalidation beside the schedule command's own.
        Assert.Equal(1, await context.AuditLogs.CountAsync(entry => entry.Action == "artist.published", Ct));
        Assert.Equal(2, await context.OutboxMessages
            .CountAsync(message => message.Type == SiteRevalidateRequested.MessageType, Ct));
    }

    [Fact]
    public async Task APassThatFindsNothingDueTouchesNothing()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var time = new FakeTimeProvider(Start);
        await using var module = Module(database, time);
        var artistId = await ScheduleAnArtistAsync(module, time);

        // Still an hour before its time.
        var summary = await Service(module, out _).PublishDueAsync(Ct);

        Assert.Equal(new ScheduledPublishSummary(0, 0, 0, 0, 0), summary);
        await using var context = database.NewContext();
        Assert.Equal(
            PublishStatus.Scheduled,
            (await context.Set<Artist>().AsNoTracking().SingleAsync(row => row.Id == artistId, Ct)).Status);
    }

    private static async Task<Guid> ScheduleAnArtistAsync(ServiceProvider services, FakeTimeProvider time)
    {
        using var scope = services.CreateScope();
        var facade = scope.ServiceProvider.GetRequiredService<IArtistsFacade>();
        var created = await facade.CreateAsync(new ArtistCreateViewModel("Jane Doe"), Editor, Ct);
        var scheduled = await facade.ScheduleAsync(
            created.Value!.Id, new ContentScheduleViewModel(time.GetUtcNow().AddMinutes(1)), created.Value.ETag, Editor, Ct);

        Assert.True(scheduled.Succeeded);
        Assert.Equal(PublishStatus.Scheduled, scheduled.Value!.Status);
        return created.Value.Id;
    }

    /// <summary>The Worker's composition of one content module, over the shared test database.</summary>
    private static ServiceProvider Module(
        SqliteArtsDatabase database, FakeTimeProvider time, IInterceptor? interceptor = null)
    {
        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddScoped(_ => interceptor is null ? database.NewContext() : database.NewContext(interceptor));
        collection.AddArtsKernel();
        collection.AddMediaModule();
        collection.AddArtistsModule();
        collection.AddArtsRevalidation();
        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));
        return collection.BuildServiceProvider();
    }

    private static ServiceProvider Scripted(params ScriptedPublisher[] publishers)
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IClock>(new FixedClock(Start));
        collection.AddScoped<ScopeMarker>();
        foreach (var publisher in publishers)
        {
            // A wrapper per scope, so the scope disposes the wrapper and never the test's own publisher — which
            // owns a semaphore the next pass still needs (the same trap IdempotencyCleanupHostedServiceTests
            // records). The wrapper also tells the publisher which scope the unit ran in.
            collection.AddScoped<IScheduledPublisher>(services =>
                new ScopedPublisher(publisher, services.GetRequiredService<ScopeMarker>()));
        }

        return collection.BuildServiceProvider(validateScopes: true);
    }

    private static ScheduledPublishHostedService Service(ServiceProvider services, out LevelLogger logger)
    {
        logger = new LevelLogger();
        return new ScheduledPublishHostedService(
            services.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(Start), logger);
    }

    /// <summary>A content type whose due list and outcomes the test writes.</summary>
    private sealed class ScriptedPublisher(string contentType, params ScheduledPublishOutcome[] outcomes)
        : IScheduledPublisher, IDisposable
    {
        private readonly Queue<ScheduledPublishOutcome> queue = new(outcomes);
        private readonly SemaphoreSlim passed = new(0);
        private int passes;

        /// <summary>One id per scope the pass resolved a publisher in, so a test can see the scopes apart.</summary>
        public List<Guid> UnitScopes { get; } = [];

        public string ContentType => contentType;

        public Exception? Throw { get; init; }

        public Action? AfterUnit { get; set; }

        public DateTimeOffset AsOf { get; private set; }

        public int Units { get; private set; }

        public int Passes => Volatile.Read(ref passes);

        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken)
        {
            AsOf = asOf;
            Interlocked.Increment(ref passes);
            passed.Release();

            return Throw is not null
                ? throw Throw
                : Task.FromResult<IReadOnlyList<Guid>>([.. queue.Select(_ => Guid.NewGuid())]);
        }

        /// <summary>Which unit of the pass throws, 1-based; 0 for none.</summary>
        public int ThrowOnUnit { get; init; }

        public Task<ScheduledPublishOutcome> PublishDueAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
        {
            Units++;
            var outcome = queue.Dequeue();
            AfterUnit?.Invoke();

            return Units == ThrowOnUnit
                ? throw new InvalidOperationException("This record cannot be published.")
                : Task.FromResult(outcome);
        }

        public async Task WaitForPassesAsync(int expected)
        {
            while (Passes < expected)
            {
                Assert.True(await passed.WaitAsync(TimeSpan.FromSeconds(10), Ct), $"Pass {expected} never came.");
            }
        }

        public void Dispose() => passed.Dispose();
    }

    /// <summary>A scoped service with an id of its own: how a test tells one DI scope from another.</summary>
    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    /// <summary>
    /// What a scope actually resolves: it notes the scope it belongs to and hands the unit to the test's publisher.
    /// </summary>
    private sealed class ScopedPublisher(ScriptedPublisher inner, ScopeMarker marker) : IScheduledPublisher
    {
        public string ContentType => inner.ContentType;

        public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
            inner.ListDueAsync(asOf, take, cancellationToken);

        public Task<ScheduledPublishOutcome> PublishDueAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken)
        {
            inner.UnitScopes.Add(marker.Id);
            return inner.PublishDueAsync(id, asOf, cancellationToken);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>Runs <paramref name="action"/> inside the first save this context makes, before it reaches the database.</summary>
    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        private bool ran;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!ran)
            {
                ran = true;
                await action();
            }

            return result;
        }
    }

    private sealed class LevelLogger : ILogger<ScheduledPublishHostedService>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }
}
