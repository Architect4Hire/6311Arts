using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Shop.Business;
using Arts6311.Domain.Modules.Shop.Data;
using Arts6311.Domain.Modules.Shop.Data.Repositories;
using Arts6311.Domain.Modules.Shop.Facade;
using Arts6311.Domain.Modules.Shop.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Shop;

public static class ShopServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Shop module's seam. Requires <c>AddArtsKernel</c> (for the sanitizer and the shared SKU
    /// registry), the <c>ArtsDbContext</c>, <c>AddMediaModule</c> and <c>AddArtistsModule</c>: the facade asks Media
    /// about the photographs and Artists about the credit.
    /// </summary>
    public static IServiceCollection AddShopModule(this IServiceCollection services)
    {
        services.TryAddScoped<IShopFacade, ShopFacade>();
        services.TryAddScoped<IShopBusiness, ShopBusiness>();
        services.TryAddScoped<IShopDataLayer, ShopDataLayer>();
        services.TryAddScoped<IShopRepository, ShopRepository>();

        // One of several IScheduledPublisher implementations, so TryAddEnumerable: the Worker resolves them all, and
        // registering this module twice still leaves one (ADR 0036).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScheduledPublisher, ShopItemScheduledPublisher>());

        // Who still names an artist, asked by the kernel before one is deleted (ADR 0052). Enumerable for the
        // same reason: every module that holds such a reference registers one.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IContentReferenceSource, ShopContentReferenceSource>());

        return services;
    }

    /// <summary>
    /// The CSV import (ADR 0053), for the API only: an idempotent command, so it needs
    /// <c>IIdempotentCommandExecutor</c>, which only the API registers
    /// (<c>AddArtsIdempotency</c>). Requires <see cref="AddShopModule"/> and <c>AddArtistsModule</c>.
    /// </summary>
    public static IServiceCollection AddShopImport(this IServiceCollection services)
    {
        services.TryAddScoped<IShopImportFacade, ShopImportFacade>();
        return services;
    }

    /// <summary>
    /// The development shop seed, for MigrationService in Development only: the real catalogue comes from the
    /// WooCommerce import (4.x). Registers Business and below, which decide and add what is seeded. The seeder
    /// resolves artists through <c>IArtistsFacade</c>, so the host also needs <c>AddArtistsModule</c> (and Media
    /// under it), and must register this after <c>AddArtistsDevelopmentSeeder</c>: seeders run in registration order.
    /// </summary>
    public static IServiceCollection AddShopDevelopmentSeeder(this IServiceCollection services)
    {
        services.TryAddScoped<IShopBusiness, ShopBusiness>();
        services.TryAddScoped<IShopDataLayer, ShopDataLayer>();
        services.TryAddScoped<IShopRepository, ShopRepository>();
        services.AddScoped<IDataSeeder, ShopDevelopmentSeeder>();
        return services;
    }
}
