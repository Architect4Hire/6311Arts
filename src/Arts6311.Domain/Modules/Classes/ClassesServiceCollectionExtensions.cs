using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Business;
using Arts6311.Domain.Modules.Classes.Data;
using Arts6311.Domain.Modules.Classes.Data.Repositories;
using Arts6311.Domain.Modules.Classes.Facade;
using Arts6311.Domain.Modules.Classes.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Classes;

public static class ClassesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Classes module's seam. Requires <c>AddArtsKernel</c> (for the sanitizer and the shared SKU
    /// registry), the <c>ArtsDbContext</c> and <c>AddMediaModule</c>: the facade asks Media about the hero and the
    /// body's pictures.
    /// </summary>
    public static IServiceCollection AddClassesModule(this IServiceCollection services)
    {
        services.TryAddScoped<IClassesFacade, ClassesFacade>();
        services.TryAddScoped<IClassesBusiness, ClassesBusiness>();
        services.TryAddScoped<IClassesDataLayer, ClassesDataLayer>();
        services.TryAddScoped<IClassesRepository, ClassesRepository>();

        // One of several IScheduledPublisher implementations, so TryAddEnumerable: the Worker resolves them all, and
        // registering this module twice still leaves one (ADR 0036).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScheduledPublisher, ClassScheduledPublisher>());

        return services;
    }

    /// <summary>
    /// The development classes seed, for MigrationService in Development only: production classes come from the
    /// WordPress import (4.x). Registers Business and below, which decide and add what is seeded.
    /// </summary>
    public static IServiceCollection AddClassesDevelopmentSeeder(this IServiceCollection services)
    {
        services.TryAddScoped<IClassesBusiness, ClassesBusiness>();
        services.TryAddScoped<IClassesDataLayer, ClassesDataLayer>();
        services.TryAddScoped<IClassesRepository, ClassesRepository>();
        services.AddScoped<IDataSeeder, ClassDevelopmentSeeder>();
        return services;
    }
}
