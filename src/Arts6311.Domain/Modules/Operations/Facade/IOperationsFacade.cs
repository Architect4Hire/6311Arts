using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Operations.Facade;

/// <summary>The Operations module's boundary, used by the API.</summary>
public interface IOperationsFacade
{
    /// <summary>
    /// The public liveness echo behind <c>GET /api/v1/site/health-info</c>: status and the server's current
    /// instant, and nothing that would describe the host.
    /// </summary>
    Task<OperationResult<HealthInfoServiceModel>> GetHealthInfoAsync(CancellationToken cancellationToken);
}
