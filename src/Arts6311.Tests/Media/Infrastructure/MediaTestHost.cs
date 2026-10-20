using System.Data.Common;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using Arts6311.Tests.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Media.Infrastructure;

/// <summary>
/// The Media module as the API and the Worker compose it — kernel, module, the real dispatcher and the real
/// SkiaSharp codec — over a SQLite <see cref="ArtsDbContext"/>, with only the blob container faked.
/// </summary>
internal sealed class MediaTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private MediaTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time, FakeMediaBlobGateway blobs)
    {
        this.database = database;
        this.services = services;
        Time = time;
        Blobs = blobs;
    }

    public FakeTimeProvider Time { get; }

    public FakeMediaBlobGateway Blobs { get; }

    /// <param name="referencing">
    /// Adds a table with a foreign key to <see cref="MediaAsset"/> of this delete behaviour, standing in for the
    /// content modules (2.4 on) that will reference images. Restrict is the convention; Cascade is the mistake.
    /// </param>
    public static async Task<MediaTestHost> CreateAsync(
        CancellationToken cancellationToken, DeleteBehavior? referencing = null, IInterceptor? interceptor = null)
    {
        var database = await SqliteArtsDatabase.CreateAsync(
            cancellationToken,
            referencing switch
            {
                DeleteBehavior.Restrict => builder => builder.ReplaceService<IModelCustomizer, RestrictReferenceCustomizer>(),
                DeleteBehavior.Cascade => builder => builder.ReplaceService<IModelCustomizer, CascadeReferenceCustomizer>(),
                null => null,
                _ => throw new ArgumentOutOfRangeException(nameof(referencing)),
            });
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        var blobs = new FakeMediaBlobGateway();

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddScoped(_ => interceptor is null ? database.NewContext() : database.NewContext(interceptor));
        collection.AddArtsKernel();
        collection.AddMediaModule();
        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(blobs));

        return new MediaTestHost(database, collection.BuildServiceProvider(), time, blobs);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public static T Resolve<T>(IServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    public async Task<T> WithFacadeAsync<T>(Func<IMediaFacade, Task<T>> action)
    {
        using var scope = CreateScope();
        return await action(Resolve<IMediaFacade>(scope));
    }

    public Task<OperationResult<MediaAssetServiceModel>> UploadAsync(
        byte[] bytes,
        string fileName,
        string? altText = null,
        MediaCompressionPreset? preset = null,
        CancellationToken cancellationToken = default) =>
        WithFacadeAsync(facade => facade.UploadAsync(
            new MediaUploadViewModel(fileName, new MemoryStream(bytes), altText, Credit: null, preset),
            EditorId,
            cancellationToken));

    /// <summary>Runs one dispatcher pass, as the Worker would.</summary>
    public async Task<OutboxDispatchSummary> DispatchAsync(CancellationToken cancellationToken)
    {
        using var scope = CreateScope();
        return await Resolve<IOutboxDispatcher>(scope).DispatchDueAsync(cancellationToken);
    }

    /// <summary>Calls a handler directly with a hand-made envelope.</summary>
    public async Task HandleAsync(string messageType, string payloadJson, int attempt, CancellationToken cancellationToken)
    {
        using var scope = CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IOutboxMessageHandler>(messageType);
        await handler.HandleAsync(
            new OutboxMessageEnvelope(Guid.NewGuid(), messageType, payloadJson, Guid.NewGuid(), attempt), cancellationToken);
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

    public Task<MediaAsset> OriginalAsync(Guid id) =>
        QueryAsync(context => context.Set<MediaAsset>().AsNoTracking().SingleAsync(asset => asset.Id == id));

    public Task<List<MediaAsset>> RenditionsAsync(Guid id) =>
        QueryAsync(context => context.Set<MediaAsset>().AsNoTracking().Where(asset => asset.ParentAssetId == id).ToListAsync());

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }
}

/// <summary>Something that uses an image, as an artist's portrait will.</summary>
internal sealed class MediaReferenceProbe
{
    public int Id { get; set; }

    public Guid PortraitId { get; set; }
}

/// <summary>The harness model plus <see cref="MediaReferenceProbe"/>, with a foreign key to the asset.</summary>
internal abstract class ReferencingModelCustomizer(ModelCustomizerDependencies dependencies, DeleteBehavior behavior)
    : SqliteModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder.Entity<MediaReferenceProbe>(probe =>
        {
            probe.ToTable("MediaReferenceProbes");
            probe.HasKey(row => row.Id);
            probe.HasOne<MediaAsset>().WithMany().HasForeignKey(row => row.PortraitId).OnDelete(behavior);
        });
    }
}

internal sealed class RestrictReferenceCustomizer(ModelCustomizerDependencies dependencies)
    : ReferencingModelCustomizer(dependencies, DeleteBehavior.Restrict);

internal sealed class CascadeReferenceCustomizer(ModelCustomizerDependencies dependencies)
    : ReferencingModelCustomizer(dependencies, DeleteBehavior.Cascade);

/// <summary>Fails the first <c>INSERT</c> into the named table once armed, partway through a save.</summary>
internal sealed class FailInsertInto(string table) : DbCommandInterceptor
{
    public bool Fired { get; private set; }

    /// <summary>Armed from the start unless a test disarms it to set up rows first.</summary>
    public bool Armed { get; set; } = true;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Check(command);
        return ValueTask.FromResult(result);
    }

    private void Check(DbCommand command)
    {
        if (Armed && !Fired && command.CommandText.Contains($"INSERT INTO \"{table}\"", StringComparison.OrdinalIgnoreCase))
        {
            Fired = true;
            throw new InvalidOperationException("Injected failure partway through the save.");
        }
    }
}
