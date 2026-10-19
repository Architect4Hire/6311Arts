using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Operations.Business;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Operations.Facade;

/// <remarks>Neither operation takes input, so there is nothing to validate and nothing to cache.</remarks>
internal sealed class OperationsFacade(IOperationsBusiness business) : IOperationsFacade
{
    public async Task<OperationResult<PingRequestedServiceModel>> RequestPingAsync(CancellationToken cancellationToken) =>
        OperationResult.Success(await business.RequestPingAsync(cancellationToken));

    public async Task<OperationResult> RecordPingHandledAsync(
        Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        await business.RecordPingHandledAsync(pingId, outboxMessageId, cancellationToken);
        return OperationResult.Success();
    }
}
