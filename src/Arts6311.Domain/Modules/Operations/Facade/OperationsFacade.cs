using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Operations.Business;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Operations.Facade;

/// <remarks>No operation here takes input, so there is nothing to validate and nothing to cache.</remarks>
internal sealed class OperationsFacade(IOperationsBusiness business) : IOperationsFacade
{
    /// <summary>
    /// Synchronous underneath — the clock is not I/O — but the facade boundary stays <c>Task</c>-shaped so the
    /// controller seam, and any later caching or policy check here, does not change the contract.
    /// </summary>
    public Task<OperationResult<HealthInfoServiceModel>> GetHealthInfoAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success(business.GetHealthInfo()));

    public async Task<OperationResult<PingRequestedServiceModel>> RequestPingAsync(CancellationToken cancellationToken) =>
        OperationResult.Success(await business.RequestPingAsync(cancellationToken));

    public async Task<OperationResult> RecordPingHandledAsync(
        Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        await business.RecordPingHandledAsync(pingId, outboxMessageId, cancellationToken);
        return OperationResult.Success();
    }
}
