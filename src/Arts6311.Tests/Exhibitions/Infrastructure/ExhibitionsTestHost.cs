using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions;
using Arts6311.Domain.Modules.Exhibitions.Data.Entities;
using Arts6311.Domain.Modules.Exhibitions.Facade;
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

namespace Arts6311.Tests.Exhibitions.Infrastructure;

/// <summary>
/// The Exhibitions module as the API composes it — kernel, Media (asked about heroes), Artists (asked about credits)
/// and Exhibitions — over a SQLite <see cref="ArtsDbContext"/>, with only the blob container faked.
/// </summary>
internal sealed class ExhibitionsTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    public const string AdminId = "admin-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private ExhibitionsTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    /// <param name="withSeeders">Registers the artist roster's and the exhibitions' development seeders, in that order, as MigrationService does.</param>
    public static async Task<ExhibitionsTestHost> CreateAsync(
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
        collection.AddExhibitionsModule();
        if (withSeeders)
        {
            collection.AddArtistsDevelopmentSeeder();
            collection.AddExhibitionsDevelopmentSeeder();
        }

        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));

        return new ExhibitionsTestHost(database, collection.BuildServiceProvider(), time);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public async Task<T> WithFacadeAsync<T>(Func<IExhibitionsFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IExhibitionsFacade>());
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

    /// <summary>An artist on the roster, straight into the Artists table: the Artists module's own rules are tested there.</summary>
    public async Task<Guid> AddArtistAsync(string name, PublishStatus status = PublishStatus.Published)
    {
        var artist = new Artist
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = Slugs.FromName(name),
            Active = true,
            Status = status,
            CreatedAt = Time.GetUtcNow(),
            UpdatedAt = Time.GetUtcNow(),
            UpdatedBy = EditorId,
            RowVersion = Guid.NewGuid(),
        };
        await ExecuteAsync(async context =>
        {
            context.Add(artist);
            await context.SaveChangesAsync();
        });
        return artist.Id;
    }

    /// <summary>An exhibition row straight into the table, for repository tests that need a particular shape.</summary>
    public Task<Guid> AddExhibitionAsync(
        string title, DateOnly start, PublishStatus status = PublishStatus.Draft, params Guid[] artistIds) =>
        AddExhibitionAsync(title, Slugs.FromName(title) + "-" + Guid.NewGuid().ToString("N")[..6], start, status, artistIds);

    public async Task<Guid> AddExhibitionAsync(
        string title, string slug, DateOnly start, PublishStatus status, IReadOnlyList<Guid> artistIds)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(async context =>
        {
            context.Add(new Exhibition
            {
                Id = id,
                Title = title,
                Slug = slug,
                StartDate = start,
                EndDate = start.AddDays(30),
                Status = status,
                Artists = [.. artistIds.Select((artistId, position) => new ExhibitionArtist { ExhibitionId = id, ArtistId = artistId, Position = position })],
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                UpdatedBy = EditorId,
                RowVersion = Guid.NewGuid(),
            });
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

    public Task<Exhibition?> StoredAsync(Guid id) =>
        QueryAsync(context => context.Set<Exhibition>().AsNoTracking().Include(exhibition => exhibition.Artists)
            .SingleOrDefaultAsync(exhibition => exhibition.Id == id));

    public Task<List<string>> AuditActionsAsync() =>
        QueryAsync(context => context.AuditLogs.AsNoTracking().Select(entry => entry.Action).ToListAsync());

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }
}
