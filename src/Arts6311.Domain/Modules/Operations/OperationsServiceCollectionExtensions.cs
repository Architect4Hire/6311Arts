using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Modules.Operations.Business;
using Arts6311.Domain.Modules.Operations.Data;
using Arts6311.Domain.Modules.Operations.Data.Repositories;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Modules.Operations;

public static class OperationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Operations module and its outbox handler. Both the API (which stages) and the Worker (which
    /// handles) call it. Requires the shared kernel (<c>AddArtsKernel</c>).
    /// </summary>
    public static IServiceCollection AddOperationsModule(this IServiceCollection services)
    {
        services.TryAddScoped<IOperationsFacade, OperationsFacade>();
        services.TryAddScoped<IOperationsBusiness, OperationsBusiness>();
        services.TryAddScoped<IOperationsDataLayer, OperationsDataLayer>();
        services.TryAddScoped<IOperationsRepository, OperationsRepository>();
        services.TryAddKeyedScoped<IOutboxMessageHandler, SystemPingRequestedHandler>(SystemPingRequested.MessageType);
        return services;
    }
}
