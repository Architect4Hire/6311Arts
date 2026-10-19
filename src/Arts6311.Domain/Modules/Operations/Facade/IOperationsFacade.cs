using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Operations.Facade;

/// <summary>The Operations module's boundary, used by the API and by this module's outbox handler.</summary>
public interface IOperationsFacade
{
    /// <summary>
    /// The public liveness echo behind <c>GET /api/v1/site/health-info</c>: status and the server's current
    /// instant, and nothing that would describe the host.
    /// </summary>
    Task<OperationResult<HealthInfoServiceModel>> GetHealthInfoAsync(CancellationToken cancellationToken);

    /// <summary>Records the request and stages <c>system.ping-requested</c>, committed together (prompt 1.5 scaffolding).</summary>
    Task<OperationResult<PingRequestedServiceModel>> RequestPingAsync(CancellationToken cancellationToken);

    /// <summary>Records that the ping arrived. Idempotent: a redelivery of the same ping records nothing more.</summary>
    Task<OperationResult> RecordPingHandledAsync(Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken);
}
