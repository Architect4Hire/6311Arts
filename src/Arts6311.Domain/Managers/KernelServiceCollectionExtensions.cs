using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Caching;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Domain.Managers;

public static class KernelServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared kernel services every host uses: <c>IClock</c>, <c>IAppCache</c>, <c>IAuditWriter</c>,
    /// <c>ICorrelationContext</c>, <c>IOutboxWriter</c> and <c>IOutboxDispatcher</c>.
    /// The <c>ArtsDbContext</c> itself is registered by each host through the Aspire SQL Server EF integration.
    /// </summary>
    public static IServiceCollection AddArtsKernel(this IServiceCollection services)
    {
        services.AddArtsTime();
        services.AddArtsCaching();
        services.AddArtsAudit();
        services.AddArtsCorrelation();
        services.AddArtsOutbox();
        return services;
    }
}
