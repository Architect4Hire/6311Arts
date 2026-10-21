using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Instructors;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Facade;
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

namespace Arts6311.Tests.Instructors.Infrastructure;

/// <summary>
/// The Instructors module as the API composes it — kernel, Media (which it asks about portraits), Artists (which it asks
/// about the artist link) and Instructors — over a SQLite <see cref="ArtsDbContext"/>, with only the blob container faked.
/// </summary>
internal sealed class InstructorsTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    public const string AdminId = "admin-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private InstructorsTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    public static async Task<InstructorsTestHost> CreateAsync(
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
        collection.AddInstructorsModule();
        if (withSeeder)
        {
            // In MigrationService's order: the roster first, so the instructor seed finds the artists it links.
            collection.AddArtistsDevelopmentSeeder();
            collection.AddInstructorsDevelopmentSeeder();
        }

        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));

        return new InstructorsTestHost(database, collection.BuildServiceProvider(), time);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public async Task<T> WithFacadeAsync<T>(Func<IInstructorsFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IInstructorsFacade>());
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var scope = CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
        {
            await seeder.SeedAsync(cancellationToken);
        }
    }

    /// <summary>An artist on the roster, written straight to the table, for an instructor to be linked to.</summary>
    public async Task<Guid> AddArtistAsync(string name, PublishStatus status = PublishStatus.Published)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(async context =>
        {
            context.Add(new Artist
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
                FileName = "portrait.jpg",
                MimeType = "image/jpeg",
                Bytes = 1000,
                Width = 800,
                Height = 1000,
                Checksum = new string('a', 64),
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

    /// <summary>An instructor row straight into the table, for repository tests that need a particular shape.</summary>
    public Task AddInstructorAsync(string name, string slug, PublishStatus status = PublishStatus.Draft, long? legacyId = null) =>
        ExecuteAsync(async context =>
        {
            context.Add(NewInstructor(name, slug, status, legacyId));
            await context.SaveChangesAsync();
        });

    public Instructor NewInstructor(string name, string slug, PublishStatus status = PublishStatus.Draft, long? legacyId = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Slug = slug,
        Active = true,
        Status = status,
        LegacyWordPressId = legacyId,
        CreatedAt = Time.GetUtcNow(),
        UpdatedAt = Time.GetUtcNow(),
        UpdatedBy = EditorId,
        RowVersion = Guid.NewGuid(),
    };

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

    public Task<Instructor?> StoredAsync(Guid id) =>
        QueryAsync(context => context.Set<Instructor>().AsNoTracking().SingleOrDefaultAsync(instructor => instructor.Id == id));

    public Task<List<string>> AuditActionsAsync() =>
        QueryAsync(context => context.AuditLogs.AsNoTracking().Select(entry => entry.Action).ToListAsync());

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }
}
