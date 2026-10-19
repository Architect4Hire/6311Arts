using System.Diagnostics;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Kernel;

/// <summary>ADR 0019: the handler runs in the enqueuing request's trace, as a child of the span that enqueued it.</summary>
public sealed class OutboxTracingTests : IAsyncLifetime
{
    private const string MessageType = "test.trace-requested";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero));
    private readonly ObservingHandler handler = new();
    private SqliteArtsDatabase database = null!;
    private ServiceProvider handlers = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        database = await SqliteArtsDatabase.CreateAsync(Ct);
        handlers = new ServiceCollection()
            .AddKeyedSingleton<IOutboxMessageHandler>(MessageType, handler)
            .BuildServiceProvider(validateScopes: true);
    }

    public async ValueTask DisposeAsync()
    {
        await handlers.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task EnqueueCapturesTheCurrentTraceparent()
    {
        using var request = W3CActivity("request");

        var id = await EnqueueAsync(new CorrelationContext().Current);

        Assert.Equal(request.Id, (await ReadAsync(id)).TraceParent);
    }

    /// <summary>
    /// A caller that passes a correlation id other than the current trace's would otherwise give the handler a
    /// correlation id that is not the message's. The traceparent is dropped instead, and the message's id wins.
    /// </summary>
    [Fact]
    public async Task AnExplicitCorrelationIdFromAnotherTraceWinsOverTheCurrentSpan()
    {
        var correlationId = Guid.NewGuid();
        Guid id;
        using (W3CActivity("request"))
        {
            id = await EnqueueAsync(correlationId);
        }

        Assert.Null((await ReadAsync(id)).TraceParent);

        Activity.Current = null;
        await DispatchAsync();
        Assert.Equal(correlationId, Assert.Single(handler.Seen).CorrelationId);
    }

    [Fact]
    public async Task WithNothingTracingThereIsNoTraceparent()
    {
        Activity.Current = null;

        var id = await EnqueueAsync();

        Assert.Null((await ReadAsync(id)).TraceParent);
    }

    [Fact]
    public async Task TheHandlerRunsAsAChildOfTheEnqueuingSpan()
    {
        ActivityTraceId traceId;
        ActivitySpanId spanId;
        using (var request = W3CActivity("request"))
        {
            (traceId, spanId) = (request.TraceId, request.SpanId);

            // What a DataLayer passes: the request's own correlation id, which is its trace id.
            await EnqueueAsync(new CorrelationContext().Current);
        }

        Activity.Current = null;
        await DispatchAsync();

        var seen = Assert.Single(handler.Seen);
        Assert.Equal(traceId, seen.TraceId);
        Assert.Equal(spanId, seen.ParentSpanId);
        Assert.Equal($"outbox.process {MessageType}", seen.OperationName);

        // So the handler's own correlation id — for its audit rows and follow-up messages — is the message's.
        Assert.Equal(seen.Envelope.CorrelationId, seen.CorrelationId);
    }

    [Fact]
    public async Task WithoutATraceparentTheHandlerStillJoinsTheCorrelationIdsTrace()
    {
        Activity.Current = null;
        var correlationId = Guid.NewGuid();
        await EnqueueAsync(correlationId);

        await DispatchAsync();

        var seen = Assert.Single(handler.Seen);
        Assert.Equal(correlationId.ToString("N"), seen.TraceId.ToHexString());
        Assert.Equal(correlationId, seen.CorrelationId);
    }

    [Fact]
    public async Task ARecordingListenerGetsAConsumerSpanWithIdsOnlyAndTheErrorTypeOnly()
    {
        var recorded = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OutboxDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (recorded) { recorded.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);
        handler.Throw = new InvalidOperationException("Push failed (HttpRequestException).");

        ActivityTraceId traceId;
        using (var request = W3CActivity("request"))
        {
            traceId = request.TraceId;
            await EnqueueAsync(new CorrelationContext().Current);
        }

        await DispatchAsync();

        // Other tests may be dispatching in parallel; this trace's span is the one to check.
        Activity span;
        lock (recorded)
        {
            span = Assert.Single(recorded, activity => activity.TraceId == traceId);
        }

        Assert.Equal(ActivityKind.Consumer, span.Kind);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(nameof(InvalidOperationException), span.StatusDescription);
        Assert.Equal(MessageType, span.GetTagItem("messaging.destination.name"));
        Assert.Equal(1, span.GetTagItem("outbox.attempt"));
    }

    private static Activity W3CActivity(string name) => new Activity(name).SetIdFormat(ActivityIdFormat.W3C).Start();

    private async Task<Guid> EnqueueAsync(Guid? correlationId = null)
    {
        await using var context = database.NewContext();
        new OutboxWriter(context, new SystemClock(time)).Enqueue(MessageType, "{}", correlationId ?? Guid.NewGuid());
        var id = context.ChangeTracker.Entries<OutboxMessage>().Single().Entity.Id;
        await context.SaveChangesAsync(Ct);
        return id;
    }

    private async Task DispatchAsync()
    {
        await using var context = database.NewContext();
        await new OutboxDispatcher(context, handlers.GetRequiredService<IServiceScopeFactory>(), new SystemClock(time))
            .DispatchDueAsync(Ct);
    }

    private async Task<OutboxMessage> ReadAsync(Guid id)
    {
        await using var context = database.NewContext();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, Ct);
    }

    private sealed record Observation(
        OutboxMessageEnvelope Envelope, string OperationName, ActivityTraceId TraceId, ActivitySpanId ParentSpanId, Guid CorrelationId);

    private sealed class ObservingHandler : IOutboxMessageHandler
    {
        public List<Observation> Seen { get; } = [];

        public Exception? Throw { get; set; }

        public Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
        {
            var current = Activity.Current!;
            Seen.Add(new Observation(
                message, current.OperationName, current.TraceId, current.ParentSpanId, new CorrelationContext().Current));
            return Throw is null ? Task.CompletedTask : throw Throw;
        }
    }
}
