using Arts6311.Domain.Modules.Operations.Business;
using Arts6311.Domain.Modules.Operations.Facade;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Operations;

public static class OperationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Operations module. Requires the shared kernel (<c>AddArtsKernel</c>). It has no outbox handler
    /// today: the 1.5 ping and its handler were removed in 1.12, and replay/discard arrive with the Sync screen.
    /// </summary>
    public static IServiceCollection AddOperationsModule(this IServiceCollection services)
    {
        services.TryAddScoped<IOperationsFacade, OperationsFacade>();
        services.TryAddScoped<IOperationsBusiness, OperationsBusiness>();
        return services;
    }
}
