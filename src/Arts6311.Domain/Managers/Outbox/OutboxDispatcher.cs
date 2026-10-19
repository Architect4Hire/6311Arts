using System.Diagnostics;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Domain.Managers.Outbox;

/// <remarks>
/// Each claimed message is processed inside an <c>outbox.process</c> consumer span whose parent is the enqueuing
/// span (<see cref="OutboxMessage.TraceParent"/>), so the handler, its SQL and its follow-up messages all join the
/// request's trace, and <c>ICorrelationContext.Current</c> inside the handler is the message's
/// <see cref="OutboxMessage.CorrelationId"/> (ADR 0019).
/// </remarks>
internal sealed class OutboxDispatcher(ArtsDbContext context, IServiceScopeFactory scopes, IClock clock) : IOutboxDispatcher
{
    /// <summary>Recorded when a message's last attempt never reported back, so nothing else is known about it.</summary>
    internal const string AbandonedError =
        "Every attempt was claimed but none recorded an outcome (the Worker stopped or crashed mid-handler).";

    public async Task<OutboxDispatchSummary> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var leaseToken = Guid.NewGuid();

        // Claimable: newly due Pending rows, or Leased rows whose lease expired (the dispatch pass or
        // process that held them died before recording an outcome) — the crash-recovery/restart path.
        // Filtering and ordering by the DateTimeOffset columns happens client-side, not in SQL: SQL Server could,
        // but SQLite's provider cannot translate DateTimeOffset comparisons, nor even ORDER BY on one, and the
        // kernel tests run there. Only the Status filter — translatable on every provider — runs server-side.
        var pendingCandidates = await context.OutboxMessages
            .Where(message => message.Status == OutboxMessageStatus.Pending)
            .ToListAsync(cancellationToken);
        var leasedCandidates = await context.OutboxMessages
            .Where(message => message.Status == OutboxMessageStatus.Leased)
            .ToListAsync(cancellationToken);

        var due = pendingCandidates.Where(message => message.AvailableAt <= now)
            .Concat(leasedCandidates.Where(message => message.LeaseExpiresAt < now))
            .OrderBy(message => message.AvailableAt)
            .Take(OutboxPolicy.BatchSize)
            .ToList();

        var claimed = 0;
        var completed = 0;
        var retrying = 0;
        var poisoned = 0;

        // Each message is claimed only when its turn comes, not the whole batch up front: its lease then runs from
        // when its handler starts, not from when an earlier message's did, and a shutdown mid-batch leaves the
        // unstarted ones exactly as they were — unclaimed, and with no attempt spent on them.
        foreach (var message in due)
        {
            if (!await TryClaimAsync(message, leaseToken, cancellationToken))
            {
                continue;
            }

            claimed++;

            // Started after the claim, so a lost claim adds nothing to the request's trace, and disposed at the end of
            // this iteration, so the outcome save is inside it.
            using var activity = StartProcessActivity(message);

            // Reclaimed with every attempt already spent: each one was claimed and none reported back, so the handler
            // is presumably what keeps taking the Worker down. Running it again would only crash-loop.
            if (message.Attempts > OutboxPolicy.MaxAttempts)
            {
                message.Attempts = OutboxPolicy.MaxAttempts;
                message.LastError = AbandonedError;
                Release(message, OutboxMessageStatus.Poisoned);
                activity.SetStatus(ActivityStatusCode.Error, "abandoned");

                if (await TryRecordAsync(message, cancellationToken))
                {
                    poisoned++;
                }

                continue;
            }

            Outcome outcome;

            try
            {
                // A scope of its own per message: the handler's DbContext is never this one, so nothing the handler
                // tracks can be flushed by this dispatcher's saves, and a failed handler's half-made changes are
                // thrown away with its scope instead of committing with the outcome.
                await using var scope = scopes.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetKeyedService<IOutboxMessageHandler>(message.Type)
                    ?? throw new InvalidOperationException($"No outbox handler is registered for message type '{message.Type}'.");

                await handler.HandleAsync(
                    new OutboxMessageEnvelope(message.Id, message.Type, message.PayloadJson, message.CorrelationId, message.Attempts),
                    cancellationToken);

                message.CompletedAt = clock.UtcNow;
                Release(message, OutboxMessageStatus.Completed);
                outcome = Outcome.Completed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Only this pass's own cancellation (shutdown) escapes: the message stays Leased, its attempt already
                // counted, and the lease brings it back. Any other cancellation — an HttpClient timeout is a
                // TaskCanceledException — is the handler failing, and retries like any other failure.
                // The handler contract (IOutboxMessageHandler) is that ex.Message is already sanitized. The span gets
                // only the exception type: telemetry leaves this process, and the contract is no excuse to widen that.
                message.LastError = Truncate(ex.Message);
                activity.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);

                // A handler that names the message unprocessable (OutboxPoisonException) is believed now: retrying a
                // malformed payload only spends four more attempts reaching the same answer.
                if (ex is OutboxPoisonException || message.Attempts >= OutboxPolicy.MaxAttempts)
                {
                    Release(message, OutboxMessageStatus.Poisoned);
                    outcome = Outcome.Poisoned;
                }
                else
                {
                    message.AvailableAt = clock.UtcNow + OutboxPolicy.BackoffFor(message.Attempts);
                    Release(message, OutboxMessageStatus.Pending);
                    outcome = Outcome.Retrying;
                }
            }

            // Saved per message: one message's exception must not roll back another message's already-decided outcome.
            if (!await TryRecordAsync(message, cancellationToken))
            {
                continue;
            }

            switch (outcome)
            {
                case Outcome.Completed:
                    completed++;
                    break;
                case Outcome.Poisoned:
                    poisoned++;
                    break;
                default:
                    retrying++;
                    break;
            }
        }

        return new OutboxDispatchSummary(claimed, completed, retrying, poisoned);
    }

    /// <summary>
    /// The claim is its own UPDATE, guarded by the state this pass read. Status and LeasedBy are concurrency
    /// tokens, so the UPDATE names both in its WHERE clause: if another dispatcher claimed the row since it was
    /// read, it matches nothing, EF raises DbUpdateConcurrencyException, and this pass leaves the message to
    /// whoever won. Without that guard two Worker replicas that read the same due row would both dispatch it —
    /// harmless for a handler that is idempotent, and a double delivery for the first one that calls anything
    /// outside this database.
    /// </summary>
    /// <remarks>
    /// The attempt is counted here, in the claim, not with the outcome: a handler that takes the process down never
    /// reaches the outcome save, and an attempt counted only there would let that message be reclaimed forever.
    /// </remarks>
    private async Task<bool> TryClaimAsync(OutboxMessage message, Guid leaseToken, CancellationToken cancellationToken)
    {
        message.Status = OutboxMessageStatus.Leased;
        message.LeasedBy = leaseToken;
        message.LeaseExpiresAt = clock.UtcNow + OutboxPolicy.LeaseDuration;
        message.Attempts++;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lost the race. Detached rather than reverted: the row is no longer in the state this pass
            // read, so there is nothing true left to track, and a still-Modified entity would be written
            // again by the next save.
            context.Entry(message).State = EntityState.Detached;
            return false;
        }
    }

    private async Task<bool> TryRecordAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The lease ran out while the handler worked and another dispatcher has taken the row: the same
            // guard as the claim, quoting this pass's own lease token. The outcome is theirs to record now,
            // so this one is dropped rather than written over it.
            context.Entry(message).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>
    /// The consumer span for one delivery. When no listener records it (a test, a host that did not add
    /// <see cref="OutboxDiagnostics.ActivitySourceName"/>), an unrecorded activity with the same parent is started
    /// instead: <see cref="Activity.Current"/> must still carry the message's trace id, because that is where the
    /// handler's correlation id comes from.
    /// </summary>
    private static Activity StartProcessActivity(OutboxMessage message)
    {
        var name = $"outbox.process {message.Type}";
        var parent = ParentOf(message);
        var activity = OutboxDiagnostics.Source.StartActivity(name, ActivityKind.Consumer, parent)
            ?? new Activity(name).SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags).Start();

        // OpenTelemetry messaging semantic conventions; ids and the type only.
        activity.SetTag("messaging.system", "outbox");
        activity.SetTag("messaging.operation.type", "process");
        activity.SetTag("messaging.destination.name", message.Type);
        activity.SetTag("messaging.message.id", message.Id.ToString());
        activity.SetTag("outbox.attempt", message.Attempts);
        return activity;
    }

    /// <summary>
    /// The enqueuing span when its traceparent was captured; otherwise a remote parent on the correlation id's trace,
    /// which keeps the trace id (and so the handler's correlation id) without naming a span that was never recorded.
    /// </summary>
    private static ActivityContext ParentOf(OutboxMessage message) =>
        message.TraceParent is not null
        && ActivityContext.TryParse(message.TraceParent, traceState: null, isRemote: true, out var captured)
            ? captured
            : new ActivityContext(
                CorrelationContext.ToTraceId(message.CorrelationId),
                ActivitySpanId.CreateRandom(),
                ActivityTraceFlags.Recorded,
                isRemote: true);

    private static void Release(OutboxMessage message, OutboxMessageStatus status)
    {
        message.Status = status;
        message.LeasedBy = null;
        message.LeaseExpiresAt = null;
    }

    private enum Outcome
    {
        Completed,
        Retrying,
        Poisoned,
    }

    private static string Truncate(string value) =>
        value.Length > OutboxPolicy.LastErrorMaxLength ? value[..OutboxPolicy.LastErrorMaxLength] : value;
}
