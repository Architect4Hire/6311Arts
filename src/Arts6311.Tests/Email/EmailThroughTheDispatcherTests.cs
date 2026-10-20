using System.Collections.Concurrent;
using System.Diagnostics;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Tests.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Email;

/// <summary>
/// <c>email.send-requested</c> through the real dispatcher: the production DataLayer stages it, the dispatcher claims
/// it and resolves the keyed handler, and the outcome lands on the row. The handler tests call the handler directly;
/// these prove the parts in between, in-process and fast — the path the 1.5 ping test used to cover, and that only
/// the Docker end-to-end test would cover otherwise.
/// </summary>
public sealed class EmailThroughTheDispatcherTests
{
    private const string Correct = "a-perfectly-fine-passphrase";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequestTravelsThroughTheOutboxToTheHandlerInsideTheRequestsTrace()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        using var spans = new OutboxSpans();

        // The request: a W3C span, as ASP.NET Core would have running.
        ActivityTraceId traceId;
        ActivitySpanId requestSpanId;
        using (var request = new Activity("POST /bff/forgot-password").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            traceId = request.TraceId;
            requestSpanId = request.SpanId;

            using var scope = host.CreateScope();
            Assert.True((await IdentityTestHost.Facade(scope).RequestPasswordResetAsync(new PasswordResetRequestViewModel("m@6311arts.test"), Ct)).Succeeded);
        }

        // The Worker: nothing ambient, its own scope.
        Activity.Current = null;
        var summary = await DispatchAsync(host);

        Assert.Equal(new OutboxDispatchSummary(1, 1, 0, 0), summary);
        Assert.Single(host.Email.Sent);

        var message = await SingleMessageAsync(host);
        Assert.Equal(OutboxMessageStatus.Completed, message.Status);
        Assert.Equal(1, message.Attempts);

        // One trace: the row carries the request's trace as its correlation id and traceparent, and the handler ran
        // as a child of the request span (ADR 0019).
        Assert.Equal(traceId.ToHexString(), message.CorrelationId.ToString("N"));
        Assert.Contains(traceId.ToHexString(), message.TraceParent, StringComparison.Ordinal);

        var process = Assert.Single(spans.For(traceId));
        Assert.Equal(requestSpanId, process.ParentSpanId);
        Assert.Equal(ActivityKind.Consumer, process.Kind);
    }

    [Fact]
    public async Task AMalformedEmailMessageIsPoisonedOnItsFirstAttemptWithOnlyTheSanitizedError()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        await EnqueueAsync(host, "{ not json");

        var summary = await DispatchAsync(host);

        Assert.Equal(new OutboxDispatchSummary(1, 0, 0, 1), summary);

        var message = await SingleMessageAsync(host);
        Assert.Equal(OutboxMessageStatus.Poisoned, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.DoesNotContain("not json", message.LastError, StringComparison.Ordinal);
        Assert.Empty(host.Email.Sent);

        using var scope = host.CreateScope();
        Assert.False(await IdentityTestHost.Resolve<ArtsDbContext>(scope).EmailDeliveries.AnyAsync(Ct));
    }

    [Fact]
    public async Task AFailedSendStoresALastErrorThatNamesNoHostOrCredential()
    {
        // What every Admin will read on the Sync screen. The handler test checks the exception it throws; this checks
        // what the dispatcher actually persisted from it.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        host.Email.Throw = new InvalidOperationException("smtp://mail-host:1025 rejected AUTH for user hunter2");
        await EnqueueAsync(host, new EmailSendRequested(IdentityEmailKinds.PasswordReset, user.Id).Serialize());

        var summary = await DispatchAsync(host);

        Assert.Equal(new OutboxDispatchSummary(1, 0, 1, 0), summary);

        var message = await SingleMessageAsync(host);
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.NotNull(message.LastError);
        Assert.DoesNotContain("mail-host", message.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1025", message.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", message.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("m@6311arts.test", message.LastError, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EnqueueAsync(IdentityTestHost host, string payloadJson)
    {
        using var scope = host.CreateScope();
        IdentityTestHost.Resolve<IOutboxWriter>(scope).Enqueue(EmailSendRequested.MessageType, payloadJson, Guid.NewGuid());
        await IdentityTestHost.Resolve<ArtsDbContext>(scope).SaveChangesAsync(Ct);
    }

    private static async Task<OutboxDispatchSummary> DispatchAsync(IdentityTestHost host)
    {
        using var scope = host.CreateScope();
        return await IdentityTestHost.Resolve<IOutboxDispatcher>(scope).DispatchDueAsync(Ct);
    }

    private static async Task<OutboxMessage> SingleMessageAsync(IdentityTestHost host)
    {
        using var scope = host.CreateScope();
        return await IdentityTestHost.Resolve<ArtsDbContext>(scope).OutboxMessages.AsNoTracking()
            .SingleAsync(row => row.Type == EmailSendRequested.MessageType, Ct);
    }

    /// <summary>
    /// Records the dispatcher's <c>outbox.process</c> spans. Listeners are process-wide and tests run in parallel, so
    /// a test reads only the spans in its own trace.
    /// </summary>
    private sealed class OutboxSpans : IDisposable
    {
        private readonly ConcurrentQueue<Activity> stopped = new();
        private readonly ActivityListener listener;

        public OutboxSpans()
        {
            listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == OutboxDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
        }

        public IEnumerable<Activity> For(ActivityTraceId traceId) => stopped
            .Where(activity => activity.TraceId == traceId
                && activity.OperationName.StartsWith("outbox.process", StringComparison.Ordinal));

        public void Dispose() => listener.Dispose();
    }
}
