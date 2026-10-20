using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Revalidation;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Redirects;
using Arts6311.Domain.Modules.Redirects.Data.Entities;
using Arts6311.Domain.Modules.Redirects.Facade;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using Arts6311.Tests.Kernel;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Redirects.Infrastructure;

/// <summary>
/// The Redirects module as the API and the Worker compose it, with Artists (and Media under it) so a real rename can
/// travel the whole way: artist save → outbox → dispatcher → redirect. SQLite, with only the blob container faked.
/// </summary>
internal sealed class RedirectsTestHost : IAsyncDisposable
{
    public const string AdminId = "admin-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private RedirectsTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    public static async Task<RedirectsTestHost> CreateAsync(
        CancellationToken cancellationToken, IInterceptor? interceptor = null, bool withSeeder = false)
    {
        var database = await SqliteArtsDatabase.CreateAsync(cancellationToken);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddScoped(_ => interceptor is null ? database.NewContext() : database.NewContext(interceptor));
        collection.AddArtsKernel();
        collection.AddMediaModule();
        collection.AddArtistsModule();
        collection.AddRedirectsModule();

        // As the Worker composes it: a content save now stages site.revalidate-requested beside the slug change, so
        // a dispatch here has to be able to handle it too (ADR 0036).
        collection.AddArtsRevalidation();
        if (withSeeder)
        {
            collection.AddLegacyRedirectSeeder();
        }

        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));

        return new RedirectsTestHost(database, collection.BuildServiceProvider(), time);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public async Task<T> WithFacadeAsync<T>(Func<IRedirectsFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IRedirectsFacade>());
    }

    public async Task<T> WithArtistsAsync<T>(Func<IArtistsFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IArtistsFacade>());
    }

    /// <summary>One Worker pass, as the hosted service runs it.</summary>
    public async Task<OutboxDispatchSummary> DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(cancellationToken);
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }

    /// <summary>A slug change as the Worker would apply it, at a chosen time.</summary>
    public Task<Arts6311.Domain.Managers.Results.OperationResult> SlugChangeAsync(
        string from, string to, DateTimeOffset at, CancellationToken cancellationToken) =>
        WithFacadeAsync(facade => facade.ApplySlugChangeAsync(
            new RedirectSlugChangeViewModel("artist", Guid.Parse("0199f0a0-0000-7000-8000-00000000c0de"), from, to, at), cancellationToken));

    /// <summary>A redirect straight into the table, for shapes the rules would refuse to build.</summary>
    public Task AddAsync(string from, string to, RedirectSource source = RedirectSource.Manual, DateTimeOffset? effectiveAt = null) =>
        ExecuteAsync(async context =>
        {
            context.Add(new Redirect
            {
                Id = Guid.NewGuid(),
                FromPath = from,
                ToPath = to,
                TargetPath = RedirectPaths.TargetOf(to),
                StatusCode = 301,
                Source = source,
                EffectiveAt = effectiveAt ?? Time.GetUtcNow(),
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                UpdatedBy = AdminId,
                RowVersion = Guid.NewGuid(),
            });
            await context.SaveChangesAsync();
        });

    /// <summary>Every redirect as <c>from → to</c>, sorted, so a test can compare the whole table at a glance.</summary>
    public Task<List<string>> TableAsync() =>
        QueryAsync(async context => (await context.Set<Redirect>().AsNoTracking().ToListAsync())
            .Select(redirect => $"{redirect.FromPath} → {redirect.ToPath}")
            .Order(StringComparer.Ordinal)
            .ToList());

    public async Task<T> QueryAsync<T>(Func<ArtsDbContext, Task<T>> query)
    {
        await using var context = database.NewContext();
        return await query(context);
    }

    public async Task ExecuteAsync(Func<ArtsDbContext, Task> action)
    {
        await using var context = database.NewContext();
        await action(context);
    }

    public Task<List<string>> AuditActionsAsync() =>
        QueryAsync(context => context.AuditLogs.AsNoTracking().Select(entry => entry.Action).ToListAsync());

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }
}
