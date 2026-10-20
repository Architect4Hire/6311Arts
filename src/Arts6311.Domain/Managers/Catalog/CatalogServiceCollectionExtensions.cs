using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Catalog;

public static class CatalogServiceCollectionExtensions
{
    /// <summary>
    /// The shared SKU registry (ADR 0037). Scoped, because it stages rows on the request's <c>ArtsDbContext</c>.
    /// </summary>
    public static IServiceCollection AddArtsCatalog(this IServiceCollection services)
    {
        services.TryAddScoped<ISkuRegistry, SkuRegistry>();
        return services;
    }
}
