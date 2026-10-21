using Arts6311.Domain.Modules.Site.Facade;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Site;

public static class SiteServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Site module (ADR 0042). Requires <c>AddArtsKernel</c> (the cache) and every content module it
    /// reads through — <c>AddArtistsModule</c>, <c>AddInstructorsModule</c>, <c>AddShopModule</c>, <c>AddExhibitionsModule</c>,
    /// <c>AddClassesModule</c>, <c>AddMembershipsModule</c> and <c>AddPagesModule</c> — so it comes after them.
    /// </summary>
    /// <remarks>The API only: it answers site reads, and has no handler or anything persisted.</remarks>
    public static IServiceCollection AddSiteModule(this IServiceCollection services)
    {
        services.TryAddScoped<ISiteFacade, SiteFacade>();
        return services;
    }
}
