using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Pages.Business;
using Arts6311.Domain.Modules.Pages.Data;
using Arts6311.Domain.Modules.Pages.Data.Repositories;
using Arts6311.Domain.Modules.Pages.Facade;
using Arts6311.Domain.Modules.Pages.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Pages;

public static class PagesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Pages module's seam. Requires <c>AddArtsKernel</c> (the sanitizer), the <c>ArtsDbContext</c> and
    /// <c>AddMediaModule</c>: the facade asks Media about the pictures a body shows.
    /// </summary>
    public static IServiceCollection AddPagesModule(this IServiceCollection services)
    {
        services.TryAddScoped<IPagesFacade, PagesFacade>();
        AddBusinessAndBelow(services);
        return services;
    }

    /// <summary>
    /// The privacy and terms placeholders, for MigrationService in <strong>every</strong> environment (ADR 0034). Only
    /// Business and below: the seed has no pictures, so it asks Media nothing.
    /// </summary>
    public static IServiceCollection AddPagesPlaceholderSeeder(this IServiceCollection services)
    {
        AddBusinessAndBelow(services);
        services.AddScoped<IDataSeeder, PagePlaceholderSeeder>();
        return services;
    }

    private static void AddBusinessAndBelow(IServiceCollection services)
    {
        services.TryAddScoped<IPagesBusiness, PagesBusiness>();
        services.TryAddScoped<IPagesDataLayer, PagesDataLayer>();
        services.TryAddScoped<IPagesRepository, PagesRepository>();
    }
}
