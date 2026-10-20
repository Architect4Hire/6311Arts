using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Artists.Business;
using Arts6311.Domain.Modules.Artists.Data;
using Arts6311.Domain.Modules.Artists.Data.Repositories;
using Arts6311.Domain.Modules.Artists.Facade;
using Arts6311.Domain.Modules.Artists.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Artists;

public static class ArtistsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Artists module's seam. Requires <c>AddArtsKernel</c>, the <c>ArtsDbContext</c> and
    /// <c>AddMediaModule</c>: the facade asks Media about portraits.
    /// </summary>
    public static IServiceCollection AddArtistsModule(this IServiceCollection services)
    {
        services.TryAddScoped<IArtistsFacade, ArtistsFacade>();
        services.TryAddScoped<IArtistsBusiness, ArtistsBusiness>();
        services.TryAddScoped<IArtistsDataLayer, ArtistsDataLayer>();
        services.TryAddScoped<IArtistsRepository, ArtistsRepository>();

        return services;
    }

    /// <summary>
    /// The development roster seed, for MigrationService in Development only: production artists come from the
    /// WordPress import (4.x), which would otherwise meet these slugs. Registers only the layers the seeder goes
    /// through (Business, which decides what is seeded, and below), so MigrationService need not compose the facade
    /// and Media, which the facade asks about portraits.
    /// </summary>
    public static IServiceCollection AddArtistsDevelopmentSeeder(this IServiceCollection services)
    {
        services.TryAddScoped<IArtistsBusiness, ArtistsBusiness>();
        services.TryAddScoped<IArtistsDataLayer, ArtistsDataLayer>();
        services.TryAddScoped<IArtistsRepository, ArtistsRepository>();
        services.AddScoped<IDataSeeder, ArtistDevelopmentSeeder>();
        return services;
    }
}
