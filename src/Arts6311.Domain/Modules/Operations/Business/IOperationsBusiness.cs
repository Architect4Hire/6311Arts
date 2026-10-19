using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Operations.Business;

internal interface IOperationsBusiness
{
    HealthInfoServiceModel GetHealthInfo();

    Task<PingRequestedServiceModel> RequestPingAsync(CancellationToken cancellationToken);

    Task RecordPingHandledAsync(Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken);
}
