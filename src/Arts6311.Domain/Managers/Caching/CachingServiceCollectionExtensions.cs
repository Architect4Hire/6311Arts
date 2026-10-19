using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers HybridCache and <see cref="IAppCache"/>. In-memory only until a host also registers an
    /// <c>IDistributedCache</c> (Redis "cache"), which HybridCache then uses as its L2.
    /// </summary>
    public static IServiceCollection AddArtsCaching(this IServiceCollection services)
    {
        services.AddHybridCache();
        services.TryAddSingleton<IAppCache, HybridAppCache>();
        return services;
    }
}
