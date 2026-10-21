using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Classes;
using Arts6311.Domain.Modules.Classes.Data.Entities;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Instructors;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Tests.Kernel;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Classes.Infrastructure;

/// <summary>
/// The Classes module as the API composes it — kernel (sanitizer and SKU registry), Media (asked about the hero and
/// the body's pictures), Artists and Instructors (asked about each run's instructor) and Classes — over a SQLite
/// <see cref="ArtsDbContext"/>, with only the blob container faked.
/// </summary>
internal sealed class ClassesTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    public const string AdminId = "admin-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private ClassesTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    /// <param name="withSeeders">Registers the instructors and classes development seeders, in MigrationService's order.</param>
    public static async Task<ClassesTestHost> CreateAsync(
        CancellationToken cancellationToken, IInterceptor? interceptor = null, bool withSeeders = false)
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
        collection.AddInstructorsModule();
        collection.AddClassesModule();
        if (withSeeders)
        {
            collection.AddInstructorsDevelopmentSeeder();
            collection.AddClassesDevelopmentSeeder();
        }

        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));

        return new ClassesTestHost(database, collection.BuildServiceProvider(), time);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public async Task<T> WithFacadeAsync<T>(Func<IClassesFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IClassesFacade>());
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }

    /// <summary>A library original, as an upload would leave it, without running the upload.</summary>
    public async Task<Guid> AddImageAsync(string? altText, MediaAssetKind kind = MediaAssetKind.Original)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(async context =>
        {
            context.Add(new MediaAsset
            {
                Id = id,
                Kind = kind,
                FileName = "hero.jpg",
                MimeType = "image/jpeg",
                Bytes = 1000,
                Width = 1600,
                Height = 1000,
                Checksum = new string('b', 64),
                BlobKey = $"{id}/original.jpg",
                ProcessingStatus = MediaProcessingStatus.Ready,
                Visibility = MediaVisibility.Draft,
                AltText = altText,
                UploadedBy = EditorId,
                UploadedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                RowVersion = Guid.NewGuid(),
            });
            await context.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>An instructor written straight to the table, for a run to name.</summary>
    public async Task<Guid> AddInstructorAsync(string name, PublishStatus status = PublishStatus.Published)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(async context =>
        {
            context.Add(new Instructor
            {
                Id = id,
                Name = name,
                Slug = Slugs.FromName(name),
                Active = true,
                Status = status,
                PublishedAt = status == PublishStatus.Published ? Time.GetUtcNow() : null,
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                UpdatedBy = EditorId,
                RowVersion = Guid.NewGuid(),
            });
            await context.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>
    /// An offering row straight into the table, with one run per SKU (each with one session, a week after the last),
    /// for repository tests that need a particular shape. The SKUs are reserved too, as a real write would.
    /// </summary>
    public async Task<Guid> AddOfferingAsync(
        string title,
        PublishStatus status = PublishStatus.Draft,
        int offeringNumber = 1,
        DateTimeOffset? firstSession = null,
        params string[] skus)
    {
        var id = Guid.NewGuid();
        var starts = firstSession ?? Time.GetUtcNow().AddDays(7);
        await ExecuteAsync(async context =>
        {
            var offering = new ClassOffering
            {
                Id = id,
                Title = title,
                Slug = Slugs.FromName(title) + "-" + Guid.NewGuid().ToString("N")[..6],
                Summary = "A class.",
                OfferingNumber = offeringNumber,
                Status = status,
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                UpdatedBy = EditorId,
                RowVersion = Guid.NewGuid(),
            };

            for (var index = 0; index < skus.Length; index++)
            {
                var run = new ClassRun
                {
                    Id = Guid.NewGuid(),
                    ClassOfferingId = id,
                    DisplayTimeZone = GalleryTimeZone.Id,
                    Sku = skus[index],
                    Capacity = 10,
                    StartingPriceCents = 3000,
                };
                run.Sessions.Add(new ClassSession
                {
                    Id = Guid.NewGuid(),
                    ClassRunId = run.Id,
                    StartsAt = starts.AddDays(7 * index),
                    EndsAt = starts.AddDays(7 * index).AddHours(3),
                });
                offering.Runs.Add(run);
                context.Add(new CatalogSku
                {
                    Sku = skus[index],
                    OwnerType = CatalogOwnerTypes.ClassRun,
                    OwnerId = run.Id,
                    CreatedAt = Time.GetUtcNow(),
                });
            }

            context.Add(offering);
            await context.SaveChangesAsync();
        });
        return id;
    }

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

    public Task<ClassOffering?> StoredAsync(Guid id) =>
        QueryAsync(context => context.Set<ClassOffering>().AsNoTracking()
            .Include(offering => offering.Runs).ThenInclude(run => run.Sessions)
            .Include(offering => offering.Images)
            .SingleOrDefaultAsync(offering => offering.Id == id));

    /// <summary>Every SKU in the shared registry, with its owner, so a test can see what a write claimed.</summary>
    public Task<List<CatalogSku>> SkusAsync() =>
        QueryAsync(context => context.Set<CatalogSku>().AsNoTracking().OrderBy(row => row.Sku).ToListAsync());

    public Task<List<string>> AuditActionsAsync() =>
        QueryAsync(context => context.AuditLogs.AsNoTracking().Select(entry => entry.Action).ToListAsync());

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }
}
