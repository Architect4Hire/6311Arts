using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Idempotency;

public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IIdempotentCommandExecutor"/> for the API, with the fingerprint key validated at startup:
    /// a host that executes idempotent commands must not start without it. Requires <c>AddArtsKernel</c>.
    /// </summary>
    public static IServiceCollection AddArtsIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .Validate(options => IdempotencyOptions.IsValidKey(options.FingerprintKey),
                "Idempotency:FingerprintKey must be a base64 secret of at least 32 bytes.")
            .ValidateOnStart();

        services.TryAddScoped<IIdempotentCommandExecutor, IdempotentCommandExecutor>();
        services.TryAddScoped<IIdempotencyBusiness, IdempotencyBusiness>();
        AddStorage(services);
        return services;
    }

    /// <summary>
    /// Registers <see cref="IIdempotencyMaintenance"/> only, for the Worker's sweep. Deleting expired rows needs no
    /// fingerprint key, so the Worker is never given one.
    /// </summary>
    public static IServiceCollection AddArtsIdempotencyMaintenance(this IServiceCollection services)
    {
        services.TryAddScoped<IIdempotencyMaintenance, IdempotencyMaintenance>();
        AddStorage(services);
        return services;
    }

    private static void AddStorage(IServiceCollection services)
    {
        services.TryAddScoped<IIdempotencyDataLayer, IdempotencyDataLayer>();
        services.TryAddScoped<IIdempotencyRepository, IdempotencyRepository>();
    }
}
