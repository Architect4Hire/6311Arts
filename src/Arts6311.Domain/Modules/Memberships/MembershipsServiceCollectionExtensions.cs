using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships.Business;
using Arts6311.Domain.Modules.Memberships.Data;
using Arts6311.Domain.Modules.Memberships.Data.Repositories;
using Arts6311.Domain.Modules.Memberships.Facade;
using Arts6311.Domain.Modules.Memberships.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Memberships;

public static class MembershipsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Memberships module's seam. Requires <c>AddArtsKernel</c> and the <c>ArtsDbContext</c>, and
    /// nothing else: a plan credits no artist and shows no library image, so this module asks no other one anything.
    /// </summary>
    public static IServiceCollection AddMembershipsModule(this IServiceCollection services)
    {
        services.TryAddScoped<IMembershipsFacade, MembershipsFacade>();
        AddBusinessAndBelow(services);

        // One of several IScheduledPublisher implementations, so TryAddEnumerable: the Worker resolves them all, and
        // registering this module twice still leaves one (ADR 0036).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IScheduledPublisher, MembershipPlanScheduledPublisher>());

        return services;
    }

    /// <summary>
    /// The development membership tiers, for MigrationService in Development only: the real tiers come from the
    /// WordPress import (4.x). Registers Business and below, which decide and add what is seeded.
    /// </summary>
    public static IServiceCollection AddMembershipsDevelopmentSeeder(this IServiceCollection services)
    {
        AddBusinessAndBelow(services);
        services.AddScoped<IDataSeeder, MembershipDevelopmentSeeder>();
        return services;
    }

    private static void AddBusinessAndBelow(IServiceCollection services)
    {
        services.TryAddScoped<IMembershipsBusiness, MembershipsBusiness>();
        services.TryAddScoped<IMembershipsDataLayer, MembershipsDataLayer>();
        services.TryAddScoped<IMembershipsRepository, MembershipsRepository>();
    }
}
