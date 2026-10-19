using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Operations.Business;
using Arts6311.Domain.Modules.Operations.Data;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Operations;

/// <summary>
/// The business and facade layers behind <c>/api/v1/site/health-info</c>, each against a fake clock.
/// </summary>
/// <remarks>
/// There is no DataLayer or repository test here because there is no DataLayer: nothing is read or written. That
/// is the one deliberate departure from the <c>add-endpoint</c> seam in this route (ADR 0021), and a DataLayer
/// added only to complete the shape would be the defect rather than the fix.
/// </remarks>
public sealed class OperationsHealthInfoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void BusinessReportsTheClocksInstant()
    {
        var business = new OperationsBusiness(new UnusedDataLayer(), Clock());

        var info = business.GetHealthInfo();

        Assert.Equal("ok", info.Status);
        Assert.Equal(Now, info.ServerTimeUtc);
    }

    [Fact]
    public void BusinessTouchesNoDataLayer()
    {
        // The DataLayer throws on every call, so the test fails rather than passes if health-info ever starts
        // reading or writing. It is the assertion, not the arrangement.
        var business = new OperationsBusiness(new UnusedDataLayer(), Clock());

        _ = business.GetHealthInfo();
    }

    [Fact]
    public async Task TheFacadeWrapsTheBusinessResult()
    {
        var facade = new OperationsFacade(new OperationsBusiness(new UnusedDataLayer(), Clock()));

        var result = await facade.GetHealthInfoAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(new HealthInfoServiceModel("ok", Now), result.Value);
    }

    private static SystemClock Clock() => new(new FakeTimeProvider(Now));

    private sealed class UnusedDataLayer : IOperationsDataLayer
    {
        public Task<Guid> RecordPingRequestedAsync(Guid pingId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("health-info must not reach the DataLayer.");

        public Task<bool> PingHandledRecordedAsync(Guid pingId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("health-info must not reach the DataLayer.");

        public Task RecordPingHandledAsync(Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("health-info must not reach the DataLayer.");
    }
}
