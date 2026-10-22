using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Pages;
using Arts6311.Domain.Modules.Pages.Data.Entities;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Tests.Kernel;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Pages.Infrastructure;

/// <summary>
/// The Pages module as the API composes it — kernel (with the real sanitizer), Media and Pages — over a SQLite
/// <see cref="ArtsDbContext"/>, with only the blob container faked.
/// </summary>
internal sealed class PagesTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    public const string AdminId = "admin-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private PagesTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    /// <param name="seedOnly">Composes only what MigrationService does: the placeholder seeder, without Media or the facade.</param>
    public static async Task<PagesTestHost> CreateAsync(
        CancellationToken cancellationToken, IInterceptor? interceptor = null, bool withSeeder = false, bool seedOnly = false)
    {
        var database = await SqliteArtsDatabase.CreateAsync(cancellationToken);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddScoped(_ => interceptor is null ? database.NewContext() : database.NewContext(interceptor));
        collection.AddArtsKernel();
        if (!seedOnly)
        {
            collection.AddMediaModule();
            collection.AddPagesModule();
            collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));
        }

        if (withSeeder || seedOnly)
        {
            collection.AddPagesPlaceholderSeeder();
        }

        return new PagesTestHost(database, collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }), time);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public async Task<T> WithFacadeAsync<T>(Func<IPagesFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IPagesFacade>());
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
    public async Task<Guid> AddImageAsync(MediaAssetKind kind = MediaAssetKind.Original)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(async context =>
        {
            context.Add(new MediaAsset
            {
                Id = id,
                Kind = kind,
                FileName = "window.jpg",
                MimeType = "image/jpeg",
                Bytes = 1000,
                Width = 1600,
                Height = 1000,
                Checksum = new string('c', 64),
                BlobKey = $"{id}/original.jpg",
                ProcessingStatus = MediaProcessingStatus.Ready,
                Visibility = MediaVisibility.Draft,
                AltText = null,
                UploadedBy = EditorId,
                UploadedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                RowVersion = Guid.NewGuid(),
            });
            await context.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>An <c>&lt;img&gt;</c> as the admin's editor writes one for a library image.</summary>
    public static string Img(Guid assetId, string? alt = "The gallery's front window") =>
        alt is null ? $"<img src=\"/media/{assetId}/800.webp\">" : $"<img src=\"/media/{assetId}/800.webp\" alt=\"{alt}\">";

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

    /// <summary>The page as a query sees it: <c>null</c> once it is deleted, like every other read (ADR 0052).</summary>
    public Task<Page?> StoredAsync(Guid id) =>
        QueryAsync(context => context.Set<Page>().AsNoTracking().Include(page => page.Images)
            .SingleOrDefaultAsync(page => page.Id == id));

    /// <summary>The row itself, deleted or not: what the database still holds after a delete.</summary>
    public Task<Page?> RowAsync(Guid id) =>
        QueryAsync(context => context.Set<Page>().IgnoreQueryFilters().AsNoTracking().Include(page => page.Images)
            .SingleOrDefaultAsync(page => page.Id == id));

    public Task<List<string>> AuditActionsAsync() =>
        QueryAsync(context => context.AuditLogs.AsNoTracking().Select(entry => entry.Action).ToListAsync());

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }
}
