using Arts6311.Domain.Modules.Operations.Data;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Operations.Business;

internal sealed class OperationsBusiness(IOperationsDataLayer dataLayer) : IOperationsBusiness
{
    public async Task<PingRequestedServiceModel> RequestPingAsync(CancellationToken cancellationToken)
    {
        // The server owns the id; nothing about a ping is bound from the request.
        var pingId = Guid.NewGuid();
        var correlationId = await dataLayer.RecordPingRequestedAsync(pingId, cancellationToken);
        return new PingRequestedServiceModel(pingId, correlationId);
    }

    public async Task RecordPingHandledAsync(Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken)
    {
        // The rule: a ping is recorded as handled once, however often delivery repeats it (delivery is
        // at-least-once). Two deliveries racing past this check would both record — see OperationsRepository.
        if (await dataLayer.PingHandledRecordedAsync(pingId, cancellationToken))
        {
            return;
        }

        await dataLayer.RecordPingHandledAsync(pingId, outboxMessageId, cancellationToken);
    }
}
