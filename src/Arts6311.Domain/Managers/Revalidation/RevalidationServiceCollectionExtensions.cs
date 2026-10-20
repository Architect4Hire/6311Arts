using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Revalidation;

public static class RevalidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the one <c>site.revalidate-requested</c> handler and the facade it calls. Requires
    /// <c>AddArtsCaching</c> (part of <c>AddArtsKernel</c>).
    /// </summary>
    /// <remarks>
    /// Called by the Worker: it is the only host that dispatches the outbox. The host must also register an
    /// out-of-process <c>IDistributedCache</c> (Redis) for the invalidation to reach the API's HybridCache at all
    /// — with L1 alone each process would only invalidate its own copy (ADR 0036).
    /// </remarks>
    public static IServiceCollection AddArtsRevalidation(this IServiceCollection services)
    {
        services.TryAddScoped<ISiteRevalidationFacade, SiteRevalidationFacade>();
        services.TryAddKeyedScoped<IOutboxMessageHandler, SiteRevalidateRequestedHandler>(SiteRevalidateRequested.MessageType);
        return services;
    }
}
