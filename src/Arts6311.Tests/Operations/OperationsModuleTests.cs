using System.Diagnostics;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Operations;
using Arts6311.Domain.Modules.Operations.Business;
using Arts6311.Domain.Modules.Operations.Data;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Domain.Modules.Operations.Managers;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Operations.Managers.ServiceModels;
using Arts6311.Tests.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Operations;

/// <summary>Business, facade and handler for the 1.5 ping, and the ping's whole trip through the outbox.</summary>
public sealed class OperationsModuleTests : IAsyncLifetime
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero));
    private SqliteArtsDatabase database = null!;
    private ServiceProvider services = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        database = await SqliteArtsDatabase.CreateAsync(Ct);

        // Registered the way the API and the Worker register them.
        services = new ServiceCollection()
            .AddSingleton<TimeProvider>(time)
            .AddScoped(_ => database.NewContext())
            .AddArtsKernel()
            .AddOperationsModule()
            .BuildServiceProvider(validateScopes: true);
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task BusinessGivesEveryPingANewServerOwnedId()
    {
        var dataLayer = new FakeDataLayer();
        var business = new OperationsBusiness(dataLayer, Clock());

        var first = await business.RequestPingAsync(Ct);
        var second = await business.RequestPingAsync(Ct);

        Assert.NotEqual(Guid.Empty, first.PingId);
        Assert.NotEqual(first.PingId, second.PingId);
        Assert.Equal([first.PingId, second.PingId], dataLayer.Requested);
        Assert.Equal(dataLayer.CorrelationId, first.CorrelationId);
    }

    [Fact]
    public async Task BusinessRecordsAPingOnceHoweverOftenItIsDelivered()
    {
        var dataLayer = new FakeDataLayer();
        var business = new OperationsBusiness(dataLayer, Clock());
        var pingId = Guid.NewGuid();

        await business.RecordPingHandledAsync(pingId, Guid.NewGuid(), Ct);
        await business.RecordPingHandledAsync(pingId, Guid.NewGuid(), Ct);

        Assert.Equal([pingId], dataLayer.Handled);
    }

    [Fact]
    public async Task TheFacadeWrapsTheBusinessResult()
    {
        var facade = new OperationsFacade(new OperationsBusiness(new FakeDataLayer(), Clock()));

        var result = await facade.RequestPingAsync(Ct);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task TheHandlerIsIdempotent()
    {
        var envelope = Envelope(new SystemPingRequested(Guid.NewGuid()).Serialize());

        await HandleAsync(envelope);
        await HandleAsync(envelope);

        await using var reader = database.NewContext();
        Assert.Equal(1, await reader.AuditLogs.CountAsync(log => log.Action == OperationsAudit.PingHandled, Ct));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("garbage")]
    public async Task AMalformedPayloadThrowsAGenericErrorAndWritesNothing(string payloadJson)
    {
        var error = await Assert.ThrowsAsync<OutboxPoisonException>(() => HandleAsync(Envelope(payloadJson)));

        Assert.Equal("Malformed system ping payload.", error.Message);
        await using var reader = database.NewContext();
        Assert.Equal(0, await reader.AuditLogs.CountAsync(Ct));
    }

    [Fact]
    public async Task AFailureBelowTheHandlerIsRethrownNamingOnlyItsType()
    {
        var handler = new SystemPingRequestedHandler(new ThrowingFacade());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(Envelope(new SystemPingRequested(Guid.NewGuid()).Serialize()), Ct));

        Assert.Equal("System ping failed (TimeoutException).", error.Message);
        Assert.Null(error.InnerException);
    }

    /// <summary>
    /// The 1.5 path without the hosts: a request stages the ping, the dispatcher delivers it to the keyed handler, and
    /// the handled row carries the request's correlation id — the same trace id, carried through the outbox.
    /// </summary>
    [Fact]
    public async Task APingTravelsFromTheRequestThroughTheOutboxToItsHandler()
    {
        PingRequestedServiceModel requested;
        using (new Activity("POST api/v1/cms/ops/ping").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            await using var request = services.CreateAsyncScope();
            requested = (await request.ServiceProvider.GetRequiredService<IOperationsFacade>().RequestPingAsync(Ct)).Value!;
        }

        OutboxDispatchSummary summary;
        await using (var worker = services.CreateAsyncScope())
        {
            summary = await worker.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(Ct);
        }

        Assert.Equal(new OutboxDispatchSummary(1, 1, 0, 0), summary);
        await using var reader = database.NewContext();
        var handled = await reader.AuditLogs.SingleAsync(log => log.Action == OperationsAudit.PingHandled, Ct);
        Assert.Equal(requested.PingId.ToString(), handled.ResourceId);
        Assert.Equal(requested.CorrelationId, handled.CorrelationId);
        var message = await reader.OutboxMessages.SingleAsync(Ct);
        Assert.Equal($"outbox:{message.Id}", handled.AfterReference);
    }

    /// <summary>
    /// Through the real dispatcher: a malformed ping is Poisoned on its first attempt, and only the sanitized message
    /// is stored.
    /// </summary>
    [Fact]
    public async Task AMalformedPingIsPoisonedWithOnlyTheSanitizedError()
    {
        await using (var request = services.CreateAsyncScope())
        {
            request.ServiceProvider.GetRequiredService<IOutboxWriter>()
                .Enqueue(SystemPingRequested.MessageType, """{"pingId":"not-a-guid"}""", Guid.NewGuid());
            await request.ServiceProvider.GetRequiredService<ArtsDbContext>().SaveChangesAsync(Ct);
        }

        await using (var worker = services.CreateAsyncScope())
        {
            Assert.Equal(
                new OutboxDispatchSummary(1, 0, 0, 1),
                await worker.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(Ct));
        }

        await using var reader = database.NewContext();
        var message = await reader.OutboxMessages.SingleAsync(Ct);
        Assert.Equal(OutboxMessageStatus.Poisoned, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.Equal("Malformed system ping payload.", message.LastError);
        Assert.Equal(0, await reader.AuditLogs.CountAsync(Ct));
    }

    private async Task HandleAsync(OutboxMessageEnvelope envelope)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IOutboxMessageHandler>(SystemPingRequested.MessageType);
        await handler.HandleAsync(envelope, Ct);
    }

    private static OutboxMessageEnvelope Envelope(string payloadJson) =>
        new(Guid.NewGuid(), SystemPingRequested.MessageType, payloadJson, Guid.NewGuid(), Attempt: 1);

    private sealed class FakeDataLayer : IOperationsDataLayer
    {
        public Guid CorrelationId { get; } = Guid.NewGuid();

        public List<Guid> Requested { get; } = [];

        public Task<Guid> RecordPingRequestedAsync(Guid pingId, CancellationToken cancellationToken)
        {
            Requested.Add(pingId);
            return Task.FromResult(CorrelationId);
        }

        public List<Guid> Handled { get; } = [];

        public Task<bool> PingHandledRecordedAsync(Guid pingId, CancellationToken cancellationToken) =>
            Task.FromResult(Handled.Contains(pingId));

        public Task RecordPingHandledAsync(Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken)
        {
            Handled.Add(pingId);
            return Task.CompletedTask;
        }
    }

    /// <summary>The ping does not read the clock; health-info does, and both go through this business class.</summary>
    private static SystemClock Clock() => new(new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero)));

    private sealed class ThrowingFacade : IOperationsFacade
    {
        public Task<OperationResult<HealthInfoServiceModel>> GetHealthInfoAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<PingRequestedServiceModel>> RequestPingAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationResult> RecordPingHandledAsync(
            Guid pingId, Guid outboxMessageId, CancellationToken cancellationToken) =>
            throw new TimeoutException("db-host.internal:1433 did not answer");
    }
}
