using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Instructors.Business;
using Arts6311.Domain.Modules.Instructors.Data;
using Arts6311.Domain.Modules.Instructors.Data.Repositories;
using Arts6311.Domain.Modules.Instructors.Facade;
using Arts6311.Domain.Modules.Instructors.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Instructors;

public static class InstructorsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Instructors module's seam. Requires <c>AddArtsKernel</c>, the <c>ArtsDbContext</c>,
    /// <c>AddMediaModule</c> and <c>AddArtistsModule</c>: the facade asks Media about portraits and Artists about the
    /// linked artist.
    /// </summary>
    public static IServiceCollection AddInstructorsModule(this IServiceCollection services)
    {
        services.TryAddScoped<IInstructorsFacade, InstructorsFacade>();
        services.TryAddScoped<IInstructorsBusiness, InstructorsBusiness>();
        services.TryAddScoped<IInstructorsDataLayer, InstructorsDataLayer>();
        services.TryAddScoped<IInstructorsRepository, InstructorsRepository>();

        // One of several IScheduledPublisher implementations, so TryAddEnumerable: the Worker resolves them all,
        // and registering this module twice still leaves one (ADR 0036).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScheduledPublisher, InstructorScheduledPublisher>());

        // Who still names an artist, asked by the kernel before one is deleted (ADR 0052). Enumerable for the
        // same reason: every module that holds such a reference registers one.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IContentReferenceSource, InstructorsContentReferenceSource>());

        return services;
    }

    /// <summary>
    /// The development instructors seed, for MigrationService in Development only: production instructors come from
    /// the WordPress import (4.x), which would otherwise meet these slugs. Registers only the layers the seeder goes
    /// through (Business, which decides what is seeded, and below). The seeder links artists through
    /// <c>IArtistsFacade</c>, and the class seed names instructors through this module's facade: both are the host's
    /// own <c>AddArtistsModule</c> and <c>AddInstructorsModule</c> (ADR 0043).
    /// </summary>
    public static IServiceCollection AddInstructorsDevelopmentSeeder(this IServiceCollection services)
    {
        services.TryAddScoped<IInstructorsBusiness, InstructorsBusiness>();
        services.TryAddScoped<IInstructorsDataLayer, InstructorsDataLayer>();
        services.TryAddScoped<IInstructorsRepository, InstructorsRepository>();
        services.AddScoped<IDataSeeder, InstructorDevelopmentSeeder>();
        return services;
    }
}
