using Arts6311.Domain.Modules.Preview.Facade;
using Arts6311.Domain.Modules.Preview.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Preview;

public static class PreviewServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Preview module (ADR 0036). Requires <c>AddArtsKernel</c>, Data Protection
    /// (<c>AddArtsDataProtection</c>) and the content modules it reads through:
    /// <c>AddArtistsModule</c>, <c>AddExhibitionsModule</c>, <c>AddPagesModule</c>, <c>AddClassesModule</c> and
    /// <c>AddShopModule</c>, so it comes after them.
    /// </summary>
    /// <remarks>
    /// The API only: it mints the links and serves them. No handler, nothing persisted, so the Worker has no use
    /// for it.
    /// </remarks>
    public static IServiceCollection AddPreviewModule(this IServiceCollection services)
    {
        services.TryAddScoped<IPreviewFacade, PreviewFacade>();
        services.TryAddScoped<IPreviewTokens, PreviewTokens>();
        return services;
    }
}
