using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// Claim, replay, restart (crash recovery), backoff and poison — the behaviours the outbox exists for — against a
/// real (SQLite) database, so the concurrency-token guards are the provider's UPDATEs, not a fake's.
/// </summary>
public sealed class OutboxDispatcherTests : IAsyncLifetime
{
    private const string MessageType = "test.message-requested";

    private static readonly DateTimeOffset Start = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(Start);
    private readonly RecordingHandler handler = new();
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
    public async Task ADueMessageIsDeliveredOnceAndCompleted()
    {
        var id = await EnqueueAsync();

        var first = await DispatchAsync();
        var second = await DispatchAsync();

        Assert.Equal(new OutboxDispatchSummary(1, 1, 0, 0), first);
        Assert.Equal(0, second.Claimed);
        var envelope = Assert.Single(handler.Calls);
        Assert.Equal(id, envelope.Id);
        Assert.Equal(1, envelope.Attempt);

        var stored = await ReadAsync(id);
        Assert.Equal(OutboxMessageStatus.Completed, stored.Status);
        Assert.Equal(Start, stored.CompletedAt);
        Assert.Null(stored.LeasedBy);
        Assert.Null(stored.LeaseExpiresAt);
    }

    [Fact]
    public async Task APassClaimsAndDeliversEveryDueMessageOnce()
    {
        await EnqueueAsync(count: 3);

        var summary = await DispatchAsync();
        var again = await DispatchAsync();

        Assert.Equal(3, summary.Claimed);
        Assert.Equal(3, summary.Completed);
        Assert.Equal(0, again.Claimed);
        Assert.Equal(3, handler.Calls.Count);
    }

    [Fact]
    public async Task APassClaimsAtMostOneBatch()
    {
        await EnqueueAsync(count: OutboxPolicy.BatchSize + 1);

        var first = await DispatchAsync();
        var second = await DispatchAsync();

        Assert.Equal(OutboxPolicy.BatchSize, first.Claimed);
        Assert.Equal(1, second.Claimed);
    }

    /// <summary>
    /// The guard a second Worker replica relies on, through two real dispatchers: B runs its whole pass between
    /// A reading the message as due and A's claim reaching the database. A's claim names the state it read,
    /// matches nothing, and A leaves the message to B.
    /// </summary>
    [Fact]
    public async Task TwoDispatchersRacingForTheSameMessageDeliverItOnce()
    {
        var id = await EnqueueAsync();
        OutboxDispatchSummary? second = null;
        var raceInBeforeFirstClaim = new BeforeFirstSave(async () => second = await DispatchAsync());

        var first = await DispatchAsync(raceInBeforeFirstClaim);

        Assert.Equal(new OutboxDispatchSummary(1, 1, 0, 0), second);
        Assert.Equal(new OutboxDispatchSummary(0, 0, 0, 0), first);
        Assert.Single(handler.Calls);
        Assert.Equal(OutboxMessageStatus.Completed, (await ReadAsync(id)).Status);
    }

    /// <summary>The same guard at the EF level: two contexts that both read the message Pending cannot both lease it.</summary>
    [Fact]
    public async Task TwoContextsThatReadTheSameDueMessageCannotBothClaimIt()
    {
        var id = await EnqueueAsync();

        await using var first = database.NewContext();
        await using var second = database.NewContext();
        var seenByFirst = await first.OutboxMessages.SingleAsync(m => m.Id == id, Ct);
        var seenBySecond = await second.OutboxMessages.SingleAsync(m => m.Id == id, Ct);

        foreach (var seen in (OutboxMessage[])[seenByFirst, seenBySecond])
        {
            seen.Status = OutboxMessageStatus.Leased;
            seen.LeasedBy = Guid.NewGuid();
            seen.LeaseExpiresAt = Start + TimeSpan.FromMinutes(1);
        }

        await first.SaveChangesAsync(Ct);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));

        Assert.Equal(seenByFirst.LeasedBy, (await ReadAsync(id)).LeasedBy);
    }

    [Fact]
    public async Task AMessageWhoseLeaseExpiredWithoutAnOutcomeIsReclaimed()
    {
        // A Worker that claimed the message and then died before recording an outcome.
        var id = await EnqueueAsync();
        await LeaseAsync(id, expiresAt: Start - TimeSpan.FromMinutes(1));

        var summary = await DispatchAsync();

        Assert.Equal(new OutboxDispatchSummary(1, 1, 0, 0), summary);
        Assert.Single(handler.Calls);
        Assert.Equal(OutboxMessageStatus.Completed, (await ReadAsync(id)).Status);
    }

    [Fact]
    public async Task AMessageStillWithinItsLeaseIsNotReclaimed()
    {
        var id = await EnqueueAsync();
        await LeaseAsync(id, expiresAt: Start + TimeSpan.FromMinutes(1));

        var summary = await DispatchAsync();

        Assert.Equal(0, summary.Claimed);
        Assert.Empty(handler.Calls);
    }

    /// <summary>
    /// A handler that outlives its lease does not get to record an outcome over whoever took the message
    /// next: the outcome write quotes this pass's own lease, and is dropped when the row no longer holds it.
    /// </summary>
    [Fact]
    public async Task NoOutcomeIsRecordedForAMessageWhoseLeaseWasTakenWhileItWasHandled()
    {
        var id = await EnqueueAsync();
        var takenBy = Guid.NewGuid();
        handler.OnHandle = async _ =>
        {
            // Another dispatcher, finding the lease lapsed, takes the message while this handler is still working.
            await using var context = database.NewContext();
            var message = await context.OutboxMessages.SingleAsync(m => m.Id == id);
            message.LeasedBy = takenBy;
            message.LeaseExpiresAt = Start + TimeSpan.FromMinutes(5);
            await context.SaveChangesAsync();
        };

        var summary = await DispatchAsync();

        Assert.Equal(new OutboxDispatchSummary(1, 0, 0, 0), summary);
        var stored = await ReadAsync(id);
        Assert.Equal(OutboxMessageStatus.Leased, stored.Status);
        Assert.Equal(takenBy, stored.LeasedBy);
        Assert.Null(stored.CompletedAt);
    }

    [Fact]
    public async Task AFailedAttemptIsRetriedOnlyAfterItsBackoff()
    {
        handler.OnHandle = _ => throw new InvalidOperationException("Transient failure (TimeoutException).");
        var id = await EnqueueAsync();

        var first = await DispatchAsync();

        Assert.Equal(new OutboxDispatchSummary(1, 0, 1, 0), first);
        var stored = await ReadAsync(id);
        Assert.Equal(OutboxMessageStatus.Pending, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal(Start + OutboxPolicy.BackoffFor(1), stored.AvailableAt);
        Assert.Equal("Transient failure (TimeoutException).", stored.LastError);
        Assert.Null(stored.LeasedBy);

        time.Advance(OutboxPolicy.BackoffFor(1) - TimeSpan.FromSeconds(1));
        Assert.Equal(0, (await DispatchAsync()).Claimed);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await DispatchAsync()).Claimed);
    }

    [Fact]
    public async Task EachRetryWaitsTwiceAsLongAsTheLast()
    {
        handler.OnHandle = _ => throw new InvalidOperationException("Always fails (HttpRequestException).");
        var id = await EnqueueAsync();
        var waits = new List<TimeSpan>();

        for (var attempt = 1; attempt < OutboxPolicy.MaxAttempts; attempt++)
        {
            var failedAt = time.GetUtcNow();
            await DispatchAsync();
            var stored = await ReadAsync(id);
            waits.Add(stored.AvailableAt - failedAt);
            time.Advance(stored.AvailableAt - failedAt);
        }

        Assert.Equal(
            [TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(80)],
            waits);
    }

    [Fact]
    public async Task AMessageThatAlwaysFailsIsPoisonedAfterMaxAttemptsAndNeverClaimedAgain()
    {
        handler.OnHandle = _ => throw new InvalidOperationException("Always fails (HttpRequestException).");
        var id = await EnqueueAsync();

        for (var attempt = 1; attempt <= OutboxPolicy.MaxAttempts; attempt++)
        {
            var summary = await DispatchAsync();

            if (attempt < OutboxPolicy.MaxAttempts)
            {
                Assert.Equal(new OutboxDispatchSummary(1, 0, 1, 0), summary);
                time.Advance(OutboxPolicy.BackoffFor(attempt));
            }
            else
            {
                Assert.Equal(new OutboxDispatchSummary(1, 0, 0, 1), summary);
            }
        }

        // The handler sees each attempt numbered, so it can tell when it is on its last (outbox.md "Handling").
        Assert.Equal(Enumerable.Range(1, OutboxPolicy.MaxAttempts), handler.Calls.Select(call => call.Attempt));

        var stored = await ReadAsync(id);
        Assert.Equal(OutboxMessageStatus.Poisoned, stored.Status);
        Assert.Equal(OutboxPolicy.MaxAttempts, stored.Attempts);
        Assert.Equal("Always fails (HttpRequestException).", stored.LastError);
        Assert.Null(stored.CompletedAt);

        time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, (await DispatchAsync()).Claimed);
    }

    [Fact]
    public async Task AMessageItsHandlerCallsUnprocessableIsPoisonedOnTheFirstAttempt()
    {
        handler.OnHandle = _ => throw new OutboxPoisonException("Malformed test payload.");
        var id = await EnqueueAsync();

        var summary = await DispatchAsync();

        Assert.Equal(new OutboxDispatchSummary(1, 0, 0, 1), summary);
        var stored = await ReadAsync(id);
        Assert.Equal(OutboxMessageStatus.Poisoned, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal("Malformed test payload.", stored.LastError);
        Assert.Null(stored.LeasedBy);
    }

    [Fact]
    public async Task AnUnregisteredMessageTypeIsPoisonedInsteadOfThrowingOutOfTheDispatcher()
    {
        var id = await EnqueueAsync(messageType: "test.no-such-handler");

        for (var attempt = 1; attempt < OutboxPolicy.MaxAttempts; attempt++)
        {
            Assert.Equal(1, (await DispatchAsync()).Retrying);
            time.Advance(OutboxPolicy.BackoffFor(attempt));
        }

        Assert.Equal(1, (await DispatchAsync()).Poisoned);
        Assert.Equal(
            "No outbox handler is registered for message type 'test.no-such-handler'.", (await ReadAsync(id)).LastError);
    }

    [Fact]
    public async Task OneMessagesFailureDoesNotLoseAnothersOutcome()
    {
        handler.OnHandle = message => message.PayloadJson.Contains("fail", StringComparison.Ordinal)
            ? throw new InvalidOperationException("Failed (TimeoutException).")
            : Task.CompletedTask;
        var failing = await EnqueueAsync(payloadJson: """{"fail":true}""");
        var succeeding = await EnqueueAsync();

        var summary = await DispatchAsync();

        Assert.Equal(new OutboxDispatchSummary(2, 1, 1, 0), summary);
        Assert.Equal(OutboxMessageStatus.Pending, (await ReadAsync(failing)).Status);
        Assert.Equal(OutboxMessageStatus.Completed, (await ReadAsync(succeeding)).Status);
    }

    /// <summary>
    /// Outcomes are saved one message at a time, so when recording a later message's outcome fails outright, the
    /// earlier message's Completed is already committed — and the unrecorded one is redelivered once its lease
    /// lapses, which is why every handler must be idempotent.
    /// </summary>
    [Fact]
    public async Task AnEarlierOutcomeSurvivesALaterOutcomeSaveFailing()
    {
        await EnqueueAsync(count: 2);

        // Each message is claimed (odd saves) then recorded (even saves): the fourth is the second message's outcome.
        await Assert.ThrowsAsync<DbUpdateException>(() => DispatchAsync(new FailingSave(saveNumber: 4)));

        var statuses = await StatusesAsync();
        Assert.Equal([OutboxMessageStatus.Leased, OutboxMessageStatus.Completed], statuses.Order());

        time.Advance(OutboxPolicy.LeaseDuration + TimeSpan.FromSeconds(1));
        Assert.Equal(new OutboxDispatchSummary(1, 1, 0, 0), await DispatchAsync());
        Assert.All(await StatusesAsync(), status => Assert.Equal(OutboxMessageStatus.Completed, status));
        Assert.Equal(3, handler.Calls.Count);
    }

    [Fact]
    public async Task LastErrorIsTruncatedToItsColumnLimit()
    {
        handler.OnHandle = _ => throw new InvalidOperationException(new string('x', OutboxPolicy.LastErrorMaxLength * 2));
        var id = await EnqueueAsync();

        await DispatchAsync();

        var lastError = (await ReadAsync(id)).LastError;
        Assert.NotNull(lastError);
        Assert.Equal(OutboxPolicy.LastErrorMaxLength, lastError.Length);
    }

    /// <summary>An HttpClient timeout is a TaskCanceledException; with the pass itself not cancelled, it is just a failure.</summary>
    [Fact]
    public async Task AHandlerTimeoutIsAnOrdinaryFailureNotAShutdown()
    {
        handler.OnHandle = _ => throw new TaskCanceledException("Square push failed (TaskCanceledException).");
        var id = await EnqueueAsync();
        var other = await EnqueueAsync();

        var summary = await DispatchAsync();

        Assert.Equal(new OutboxDispatchSummary(2, 0, 2, 0), summary);
        var stored = await ReadAsync(id);
        Assert.Equal(OutboxMessageStatus.Pending, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal("Square push failed (TaskCanceledException).", stored.LastError);
        Assert.Equal(OutboxMessageStatus.Pending, (await ReadAsync(other)).Status);
    }

    [Fact]
    public async Task ShutdownStopsThePassWithTheInFlightMessageLeasedAndTheRestUnclaimed()
    {
        using var shutdown = new CancellationTokenSource();
        handler.OnHandle = _ =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };
        await EnqueueAsync(count: 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DispatchAsync(shutdown.Token));

        Assert.Single(handler.Calls);
        var rows = await ReadAllAsync();
        var inFlight = Assert.Single(rows, row => row.Status == OutboxMessageStatus.Leased);
        Assert.Equal(1, inFlight.Attempts);
        Assert.Null(inFlight.LastError);
        var untouched = Assert.Single(rows, row => row.Status == OutboxMessageStatus.Pending);
        Assert.Equal(0, untouched.Attempts);
        Assert.Null(untouched.LeasedBy);
    }

    /// <summary>
    /// A handler that takes the Worker down never reaches the outcome save. Its attempts are counted at claim, so
    /// after <see cref="OutboxPolicy.MaxAttempts"/> lapsed leases it is poisoned instead of crash-looping forever.
    /// </summary>
    [Fact]
    public async Task AHandlerThatKeepsCrashingTheWorkerIsPoisonedAfterMaxAttempts()
    {
        var id = await EnqueueAsync();

        for (var attempt = 1; attempt <= OutboxPolicy.MaxAttempts; attempt++)
        {
            // Each claim (save 1) commits; the process "dies" before the outcome (save 2) lands.
            await Assert.ThrowsAsync<DbUpdateException>(() => DispatchAsync(new FailingSave(saveNumber: 2)));
            Assert.Equal(attempt, (await ReadAsync(id)).Attempts);
            time.Advance(OutboxPolicy.LeaseDuration + TimeSpan.FromSeconds(1));
        }

        var summary = await DispatchAsync();

        Assert.Equal(new OutboxDispatchSummary(1, 0, 0, 1), summary);
        Assert.Equal(OutboxPolicy.MaxAttempts, handler.Calls.Count);
        var stored = await ReadAsync(id);
        Assert.Equal(OutboxMessageStatus.Poisoned, stored.Status);
        Assert.Equal(OutboxPolicy.MaxAttempts, stored.Attempts);
        Assert.Equal(OutboxDispatcher.AbandonedError, stored.LastError);
        Assert.Null(stored.LeasedBy);
        Assert.Null(stored.LeaseExpiresAt);
    }

    [Fact]
    public async Task EachMessagesLeaseRunsFromWhenItsOwnHandlerStarts()
    {
        var observed = new List<(DateTimeOffset HandledAt, DateTimeOffset? LeaseExpiresAt)>();
        handler.OnHandle = async message =>
        {
            observed.Add((time.GetUtcNow(), (await ReadAsync(message.Id)).LeaseExpiresAt));
            time.Advance(TimeSpan.FromMinutes(4));
        };
        await EnqueueAsync(count: 2);

        await DispatchAsync();

        Assert.Equal(2, observed.Count);
        Assert.All(observed, seen => Assert.Equal(seen.HandledAt + OutboxPolicy.LeaseDuration, seen.LeaseExpiresAt));
    }

    /// <summary>
    /// The handler's scope, and so its DbContext, is never the dispatcher's: a handler that changes entities and
    /// then fails must not have those changes committed by the dispatcher's outcome save.
    /// </summary>
    [Fact]
    public async Task AFailedHandlersOwnChangesAreNotCommittedWithItsOutcome()
    {
        await using var services = HostLikeServices(
            collection => collection.AddKeyedScoped<IOutboxMessageHandler, DirtyingHandler>(MessageType));
        var id = await EnqueueAsync();

        await using (var scope = services.CreateAsyncScope())
        {
            var summary = await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(Ct);
            Assert.Equal(new OutboxDispatchSummary(1, 0, 1, 0), summary);
        }

        await using var reader = database.NewContext();
        Assert.Equal(0, await reader.AuditLogs.CountAsync(Ct));
        Assert.Equal(OutboxMessageStatus.Pending, (await ReadAsync(id)).Status);
    }

    [Fact]
    public async Task EachMessageIsHandledInAScopeOfItsOwn()
    {
        var seen = new List<ArtsDbContext>();
        await using var services = HostLikeServices(collection => collection
            .AddSingleton(seen)
            .AddKeyedScoped<IOutboxMessageHandler, ScopeProbeHandler>(MessageType));
        await EnqueueAsync(count: 2);

        await using var scope = services.CreateAsyncScope();
        var dispatcherContext = scope.ServiceProvider.GetRequiredService<ArtsDbContext>();
        await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(Ct);

        Assert.Equal(2, seen.Count);
        Assert.NotSame(seen[0], seen[1]);
        Assert.DoesNotContain(dispatcherContext, seen);
    }

    private async Task<Guid> EnqueueAsync(string messageType = MessageType, string payloadJson = "{}")
    {
        await using var context = database.NewContext();
        new OutboxWriter(context, Clock()).Enqueue(messageType, payloadJson, Guid.NewGuid());
        var id = context.ChangeTracker.Entries<OutboxMessage>().Single().Entity.Id;
        await context.SaveChangesAsync(Ct);
        return id;
    }

    private async Task EnqueueAsync(int count)
    {
        await using var context = database.NewContext();
        var writer = new OutboxWriter(context, Clock());

        for (var index = 0; index < count; index++)
        {
            writer.Enqueue(MessageType, "{}", Guid.NewGuid());
        }

        await context.SaveChangesAsync(Ct);
    }

    private async Task LeaseAsync(Guid id, DateTimeOffset expiresAt)
    {
        await using var context = database.NewContext();
        var message = await context.OutboxMessages.SingleAsync(m => m.Id == id, Ct);
        message.Status = OutboxMessageStatus.Leased;
        message.LeasedBy = Guid.NewGuid();
        message.LeaseExpiresAt = expiresAt;
        await context.SaveChangesAsync(Ct);
    }

    private Task<OutboxDispatchSummary> DispatchAsync(params IInterceptor[] interceptors) =>
        DispatchAsync(Ct, interceptors);

    private async Task<OutboxDispatchSummary> DispatchAsync(CancellationToken cancellationToken, params IInterceptor[] interceptors)
    {
        await using var context = database.NewContext(interceptors);
        return await new OutboxDispatcher(context, handlers.GetRequiredService<IServiceScopeFactory>(), Clock())
            .DispatchDueAsync(cancellationToken);
    }

    /// <summary>Registered the way a host registers them: scoped context, kernel registrations, keyed handlers.</summary>
    private ServiceProvider HostLikeServices(Action<IServiceCollection> addHandlers)
    {
        var collection = new ServiceCollection()
            .AddSingleton<TimeProvider>(time)
            .AddArtsTime()
            .AddScoped(_ => database.NewContext())
            .AddArtsOutbox();
        addHandlers(collection);
        return collection.BuildServiceProvider(validateScopes: true);
    }

    private async Task<OutboxMessage> ReadAsync(Guid id)
    {
        await using var context = database.NewContext();
        return await context.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, Ct);
    }

    private async Task<List<OutboxMessage>> ReadAllAsync()
    {
        await using var context = database.NewContext();
        return await context.OutboxMessages.AsNoTracking().ToListAsync(Ct);
    }

    private async Task<List<OutboxMessageStatus>> StatusesAsync()
    {
        await using var context = database.NewContext();
        return await context.OutboxMessages.Select(m => m.Status).ToListAsync(Ct);
    }

    private SystemClock Clock() => new(time);

    /// <summary>The test-only handler (1.4 RESTRICTION): records every envelope and optionally misbehaves.</summary>
    private sealed class RecordingHandler : IOutboxMessageHandler
    {
        public List<OutboxMessageEnvelope> Calls { get; } = [];

        public Func<OutboxMessageEnvelope, Task>? OnHandle { get; set; }

        public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
        {
            Calls.Add(message);
            if (OnHandle is not null)
            {
                await OnHandle(message);
            }
        }
    }

    /// <summary>Stages a domain change on its own scoped context, then fails before saving it.</summary>
    private sealed class DirtyingHandler(ArtsDbContext context) : IOutboxMessageHandler
    {
        public Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
        {
            context.AuditLogs.Add(new AuditLog
            {
                Action = "artist.archived",
                ResourceType = "artist",
                ResourceId = "jane-doe",
                CorrelationId = message.CorrelationId,
                OccurredAt = Start,
                Summary = "Half-made change.",
            });

            throw new InvalidOperationException("Failed after staging (InvalidOperationException).");
        }
    }

    /// <summary>Records the scoped context each delivery was given.</summary>
    private sealed class ScopeProbeHandler(ArtsDbContext context, List<ArtsDbContext> seen) : IOutboxMessageHandler
    {
        public Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
        {
            seen.Add(context);
            return Task.CompletedTask;
        }
    }

    /// <summary>Runs <paramref name="action"/> once, just before the context's first save reaches the database.</summary>
    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        private bool ran;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!ran)
            {
                ran = true;
                await action();
            }

            return result;
        }
    }

    /// <summary>Fails the context's <paramref name="saveNumber"/>th save the way a lost connection would.</summary>
    private sealed class FailingSave(int saveNumber) : SaveChangesInterceptor
    {
        private int saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ++saves == saveNumber
                ? throw new DbUpdateException("Simulated save failure.")
                : ValueTask.FromResult(result);
    }
}
