using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships;
using Arts6311.Domain.Modules.Memberships.Data.Entities;
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Tests.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Memberships.Infrastructure;

/// <summary>
/// The Memberships module as the API composes it — the kernel and Memberships — over a SQLite
/// <see cref="ArtsDbContext"/>. Nothing is faked: the module asks no other module anything, so there is nothing to
/// stand in for.
/// </summary>
internal sealed class MembershipsTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private MembershipsTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    /// <param name="withSeeder">Registers the development tiers' seeder, as MigrationService does.</param>
    public static async Task<MembershipsTestHost> CreateAsync(
        CancellationToken cancellationToken, IInterceptor? interceptor = null, bool withSeeder = false)
    {
        var database = await SqliteArtsDatabase.CreateAsync(cancellationToken);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddScoped(_ => interceptor is null ? database.NewContext() : database.NewContext(interceptor));
        collection.AddArtsKernel();
        collection.AddMembershipsModule();
        if (withSeeder)
        {
            collection.AddMembershipsDevelopmentSeeder();
        }

        return new MembershipsTestHost(database, collection.BuildServiceProvider(), time);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public async Task<T> WithFacadeAsync<T>(Func<IMembershipsFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMembershipsFacade>());
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }

    /// <summary>A plan row straight into the table, for repository tests that need a particular shape.</summary>
    public async Task<Guid> AddPlanAsync(
        string name,
        PublishStatus status = PublishStatus.Draft,
        int displayOrder = 1,
        long? startingPriceCents = 10_000,
        params string[] perks)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(async context =>
        {
            context.Add(new MembershipPlan
            {
                Id = id,
                Name = name,
                Slug = Slugs.FromName(name) + "-" + Guid.NewGuid().ToString("N")[..6],
                DiscountPercent = 5,
                DisplayOrder = displayOrder,
                StartingPriceCents = startingPriceCents,
                StartingCadence = MembershipCadence.Annual,
                Status = status,
                Perks = [.. perks.Select((perk, position) => new MembershipPlanPerk
                {
                    MembershipPlanId = id,
                    Position = position,
                    Text = perk,
                })],
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                UpdatedBy = EditorId,
                RowVersion = Guid.NewGuid(),
            });
            await context.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>What Square would have reported, written straight to the projections as the 5.x pull will.</summary>
    public Task SquareReportedAsync(Guid id, long priceCents, string cadence) =>
        ExecuteAsync(async context =>
        {
            var plan = await context.Set<MembershipPlan>().SingleAsync(row => row.Id == id);
            plan.PriceCents = priceCents;
            plan.Cadence = cadence;
            await context.SaveChangesAsync();
        });

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

    public Task<MembershipPlan?> StoredAsync(Guid id) =>
        QueryAsync(context => context.Set<MembershipPlan>().AsNoTracking()
            .Include(plan => plan.Perks)
            .SingleOrDefaultAsync(plan => plan.Id == id));

    public Task<List<string>> AuditActionsAsync() =>
        QueryAsync(context => context.AuditLogs.AsNoTracking().Select(entry => entry.Action).ToListAsync());

    public Task<List<OutboxMessage>> OutboxAsync() =>
        QueryAsync(context => context.OutboxMessages.AsNoTracking().OrderBy(message => message.CreatedAt).ToListAsync());

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }
}
