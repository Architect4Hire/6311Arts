using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

namespace Arts6311.Tests.Api.Infrastructure;

/// <summary>
/// Stands in for the Operations module in endpoint tests, so a route that stages an outbox message can be reached
/// without a database. These tests are about the edge — authentication, policy, the response shape — and the
/// module's own behaviour is covered by its layer tests.
/// </summary>
internal sealed class FakeOperationsFacade : IOperationsFacade
{
    public PingRequestedServiceModel Ping { get; } = new(Guid.NewGuid(), Guid.NewGuid());

    public HealthInfoServiceModel HealthInfo { get; init; } = new("ok", DateTimeOffset.UnixEpoch);

    public OperationError? PingError { get; init; }

    public int PingCalls { get; private set; }

    public Task<OperationResult<HealthInfoServiceModel>> GetHealthInfoAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Success(HealthInfo));

    public Task<OperationResult<PingRequestedServiceModel>> RequestPingAsync(CancellationToken cancellationToken)
    {
        PingCalls++;
        return Task.FromResult(PingError is { } error
            ? OperationResult.Failure<PingRequestedServiceModel>(error)
            : OperationResult.Success(Ping));
    }

    public Task<OperationResult> RecordPingHandledAsync(
        Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
