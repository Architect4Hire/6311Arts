using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Exhibitions.Business;
using Arts6311.Domain.Modules.Exhibitions.Data;
using Arts6311.Domain.Modules.Exhibitions.Data.Repositories;
using Arts6311.Domain.Modules.Exhibitions.Facade;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Exhibitions;

public static class ExhibitionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Exhibitions module's seam. Requires <c>AddArtsKernel</c>, the <c>ArtsDbContext</c>,
    /// <c>AddMediaModule</c> and <c>AddArtistsModule</c>: the facade asks Media about the hero and Artists about the
    /// credits.
    /// </summary>
    public static IServiceCollection AddExhibitionsModule(this IServiceCollection services)
    {
        services.TryAddScoped<IExhibitionsFacade, ExhibitionsFacade>();
        services.TryAddScoped<IExhibitionsBusiness, ExhibitionsBusiness>();
        services.TryAddScoped<IExhibitionsDataLayer, ExhibitionsDataLayer>();
        services.TryAddScoped<IExhibitionsRepository, ExhibitionsRepository>();

        return services;
    }

    /// <summary>
    /// The development exhibitions seed, for MigrationService in Development only: production shows come from the
    /// WordPress import (4.x). Registers Business and below, which decide and add what is seeded. The seeder reads
    /// credits through <c>IArtistsFacade</c>, so the host also needs <c>AddArtistsModule</c> (and Media under it), and
    /// must register this after <c>AddArtistsDevelopmentSeeder</c>: seeders run in registration order.
    /// </summary>
    public static IServiceCollection AddExhibitionsDevelopmentSeeder(this IServiceCollection services)
    {
        services.TryAddScoped<IExhibitionsBusiness, ExhibitionsBusiness>();
        services.TryAddScoped<IExhibitionsDataLayer, ExhibitionsDataLayer>();
        services.TryAddScoped<IExhibitionsRepository, ExhibitionsRepository>();
        services.AddScoped<IDataSeeder, ExhibitionDevelopmentSeeder>();
        return services;
    }
}
