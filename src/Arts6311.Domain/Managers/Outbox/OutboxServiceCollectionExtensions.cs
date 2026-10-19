using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IOutboxWriter"/> and <see cref="IOutboxDispatcher"/>, scoped to the request's
    /// <c>ArtsDbContext</c>. Requires <c>IClock</c>. Each module registers its own
    /// <see cref="IOutboxMessageHandler"/> implementations with keyed DI, one per message type.
    /// </summary>
    public static IServiceCollection AddArtsOutbox(this IServiceCollection services)
    {
        services.TryAddScoped<IOutboxWriter, OutboxWriter>();
        services.TryAddScoped<IOutboxDispatcher, OutboxDispatcher>();
        return services;
    }
}
