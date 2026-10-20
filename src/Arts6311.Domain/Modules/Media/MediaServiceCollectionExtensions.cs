using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Modules.Media.Business;
using Arts6311.Domain.Modules.Media.Data;
using Arts6311.Domain.Modules.Media.Data.Repositories;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Domain.Modules.Media.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Media;

public static class MediaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Media module: the seam, the blob and image gateways, and both outbox handlers. Called by the
    /// API (uploads, edits, delivery) and the Worker (renditions, purges). Requires <c>AddArtsKernel</c>, the
    /// <c>ArtsDbContext</c> and the Aspire <c>BlobServiceClient</c> ("blobs").
    /// </summary>
    public static IServiceCollection AddMediaModule(this IServiceCollection services)
    {
        services.TryAddScoped<IMediaFacade, MediaFacade>();
        services.TryAddScoped<IMediaBusiness, MediaBusiness>();
        services.TryAddScoped<IMediaDataLayer, MediaDataLayer>();
        services.TryAddScoped<IMediaRepository, MediaRepository>();
        services.TryAddScoped<IMediaBlobGateway, AzureMediaBlobGateway>();

        // A singleton: it owns the per-process limit on concurrent decodes.
        services.TryAddSingleton<IImageCodecGateway, SkiaImageCodecGateway>();

        services.TryAddKeyedScoped<IOutboxMessageHandler, MediaRenditionRequestedHandler>(MediaRenditionRequested.MessageType);
        services.TryAddKeyedScoped<IOutboxMessageHandler, MediaPurgeRequestedHandler>(MediaPurgeRequested.MessageType);

        return services;
    }
}
