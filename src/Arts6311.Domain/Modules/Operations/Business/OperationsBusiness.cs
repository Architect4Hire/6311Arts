using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Operations.Data;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Operations.Business;

internal sealed class OperationsBusiness(IOperationsDataLayer dataLayer, IClock clock) : IOperationsBusiness
{
    /// <summary>
    /// A read of the injected clock, and no I/O: no persistence and no gateway call. There is deliberately no
    /// DataLayer or repository here, because there would be nothing for one to call, and an empty DataLayer added
    /// only to complete the shape of the seam would be the defect rather than the fix (ADR 0021).
    /// </summary>
    /// <remarks>
    /// Not a deterministic calculation — the instant changes on every call. The reason it may skip the DataLayer is
    /// the absence of I/O, not repeatability. ADR 0021 bounds the precedent: any database read, even of one row,
    /// goes through a DataLayer.
    /// </remarks>
    public HealthInfoServiceModel GetHealthInfo() => new("ok", clock.UtcNow);

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
