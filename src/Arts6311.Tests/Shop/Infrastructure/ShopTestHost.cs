using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists;
using Arts6311.Domain.Modules.Artists.Data.Entities;
using Arts6311.Domain.Modules.Classes;
using Arts6311.Domain.Modules.Instructors;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Shop;
using Arts6311.Domain.Modules.Shop.Data.Entities;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Tests.Kernel;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Shop.Infrastructure;

/// <summary>
/// The Shop module as the API composes it — kernel (sanitizer and SKU registry), Media (asked about photographs),
/// Artists (asked about the credit) and Shop — over a SQLite <see cref="ArtsDbContext"/>, with only the blob
/// container faked.
/// </summary>
/// <remarks>
/// The CSV import is composed as the API composes it, with the idempotency kernel only the API has (ADR 0053).
/// <para>
/// Classes is composed too, which no production host needs for Shop's sake: it is how the cross-type SKU rule is
/// tested with two real owners rather than two rows written by hand (ADR 0037, 0038).
/// </para>
/// </remarks>
internal sealed class ShopTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    public const string AdminId = "admin-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private ShopTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    /// <param name="withSeeders">Registers the artist roster's and the shop's development seeders, in that order, as MigrationService does.</param>
    public static async Task<ShopTestHost> CreateAsync(
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
        collection.AddShopModule();
        collection.AddArtsIdempotency(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Idempotency:FingerprintKey"] = Convert.ToBase64String(new byte[32]),
            })
            .Build());
        collection.AddShopImport();
        if (withSeeders)
        {
            collection.AddArtistsDevelopmentSeeder();
            collection.AddShopDevelopmentSeeder();
        }

        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));

        return new ShopTestHost(database, collection.BuildServiceProvider(), time);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public async Task<T> WithFacadeAsync<T>(Func<IShopFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IShopFacade>());
    }

    public async Task<T> WithImportFacadeAsync<T>(Func<IShopImportFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IShopImportFacade>());
    }

    /// <summary>For the cross-type SKU rule: a class session's SKU comes from the Classes module's own facade.</summary>
    public async Task<T> WithClassesFacadeAsync<T>(Func<IClassesFacade, Task<T>> action)
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
                FileName = "piece.jpg",
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

    /// <summary>
    /// A shop item row straight into the table, with its SKU reserved as a real write would, for repository tests
    /// that need a particular shape.
    /// </summary>
    public async Task<Guid> AddItemAsync(
        string title,
        PublishStatus status = PublishStatus.Draft,
        int itemNumber = 1,
        string sku = "ART-1",
        Guid? artistId = null,
        params string[] categories)
    {
        var id = Guid.NewGuid();
        await ExecuteAsync(async context =>
        {
            context.Add(new ShopItem
            {
                Id = id,
                Title = title,
                Slug = Slugs.FromName(title) + "-" + Guid.NewGuid().ToString("N")[..6],
                Description = "<p>A piece.</p>",
                ArtistId = artistId,
                Sku = sku,
                ItemNumber = itemNumber,
                StartingPriceCents = 12500,
                StartingStockQuantity = 1,
                OneOfAKind = true,
                Status = status,
                Categories = [.. categories.Select(category => new ShopItemCategory { ShopItemId = id, Category = category })],
                CreatedAt = Time.GetUtcNow(),
                UpdatedAt = Time.GetUtcNow(),
                UpdatedBy = EditorId,
                RowVersion = Guid.NewGuid(),
            });
            context.Add(new CatalogSku
            {
                Sku = sku,
                OwnerType = CatalogOwnerTypes.ShopItem,
                OwnerId = id,
                CreatedAt = Time.GetUtcNow(),
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

    public Task<ShopItem?> StoredAsync(Guid id) =>
        QueryAsync(context => context.Set<ShopItem>().AsNoTracking()
            .Include(item => item.Categories)
            .Include(item => item.Images)
            .Include(item => item.DescriptionImages)
            .SingleOrDefaultAsync(item => item.Id == id));

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
