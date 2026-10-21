using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers HybridCache and <see cref="IAppCache"/>. In-memory only until a host also registers an
    /// <c>IDistributedCache</c> (Redis "cache"), which HybridCache then uses as its L2, and calls
    /// <see cref="AddArtsCacheInvalidation"/>.
    /// </summary>
    public static IServiceCollection AddArtsCaching(this IServiceCollection services)
    {
        services.AddHybridCache();
        services.TryAddSingleton<ICacheInvalidationBus, NoCacheInvalidationBus>();
        services.TryAddSingleton<IAppCache, HybridAppCache>();
        return services;
    }

    /// <summary>
    /// For each host sharing the Redis "cache" (the API and the Worker): tags this process removes are published, and
    /// tags other processes remove are applied here (ADR 0049). Requires <c>AddRedisDistributedCache("cache")</c>,
    /// whose connection it uses, and <see cref="AddArtsCaching"/>.
    /// </summary>
    public static IServiceCollection AddArtsCacheInvalidation(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<ICacheInvalidationBus, RedisCacheInvalidationBus>());
        services.AddHostedService<CacheInvalidationListener>();
        return services;
    }
}
