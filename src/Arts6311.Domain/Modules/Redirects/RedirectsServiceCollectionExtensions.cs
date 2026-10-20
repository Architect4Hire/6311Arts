using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Redirects.Business;
using Arts6311.Domain.Modules.Redirects.Data;
using Arts6311.Domain.Modules.Redirects.Data.Repositories;
using Arts6311.Domain.Modules.Redirects.Facade;
using Arts6311.Domain.Modules.Redirects.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Redirects;

public static class RedirectsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Redirects module: the seam and the <c>redirects.slug-changed</c> handler. Called by the API (the
    /// CMS and site routes) and the Worker (the handler). Requires <c>AddArtsKernel</c> and the <c>ArtsDbContext</c>.
    /// </summary>
    public static IServiceCollection AddRedirectsModule(this IServiceCollection services)
    {
        services.TryAddScoped<IRedirectsFacade, RedirectsFacade>();
        AddBusinessAndBelow(services);
        services.TryAddKeyedScoped<IOutboxMessageHandler, RedirectSlugChangedHandler>(RedirectSlugChanged.MessageType);
        return services;
    }

    /// <summary>The spike's fixed legacy redirects, for MigrationService in every environment (ADR 0035).</summary>
    public static IServiceCollection AddLegacyRedirectSeeder(this IServiceCollection services)
    {
        AddBusinessAndBelow(services);
        services.AddScoped<IDataSeeder, LegacyRedirectSeeder>();
        return services;
    }

    private static void AddBusinessAndBelow(IServiceCollection services)
    {
        services.TryAddScoped<IRedirectsBusiness, RedirectsBusiness>();
        services.TryAddScoped<IRedirectsDataLayer, RedirectsDataLayer>();
        services.TryAddScoped<IRedirectsRepository, RedirectsRepository>();
    }
}
