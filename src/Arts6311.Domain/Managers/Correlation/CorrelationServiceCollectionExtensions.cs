using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Correlation;

public static class CorrelationServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ICorrelationContext"/>, scoped so its fallback id is fixed per request or pass.</summary>
    public static IServiceCollection AddArtsCorrelation(this IServiceCollection services)
    {
        services.TryAddScoped<ICorrelationContext, CorrelationContext>();
        return services;
    }
}
