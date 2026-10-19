using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Time;

public static class TimeServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IClock"/> over <see cref="TimeProvider.System"/>, unless a test replaced it.</summary>
    public static IServiceCollection AddArtsTime(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, SystemClock>();
        return services;
    }
}
