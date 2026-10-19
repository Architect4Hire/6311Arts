using System.Data.Common;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Operations.Data;
using Arts6311.Domain.Modules.Operations.Data.Repositories;
using Arts6311.Domain.Modules.Operations.Managers;
using Arts6311.Tests.Kernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Operations;

public sealed class OperationsDataLayerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePingRequestIsAuditedAndStagedBeforeTheOneSave()
    {
        var steps = new List<string>();
        var correlationId = Guid.NewGuid();
        var outbox = new RecordingOutbox(steps);
        var dataLayer = new OperationsDataLayer(
            new RecordingRepository(steps), new RecordingAudit(steps), outbox, new FixedCorrelation(correlationId));
        var pingId = Guid.NewGuid();

        var returned = await dataLayer.RecordPingRequestedAsync(pingId, Ct);

        Assert.Equal(["audit", "enqueue", "save"], steps);
        var staged = Assert.Single(outbox.Staged);
        Assert.Equal(SystemPingRequested.MessageType, staged.Type);
        Assert.Equal(new SystemPingRequested(pingId), SystemPingRequested.TryParse(staged.PayloadJson));
        Assert.Equal(correlationId, staged.CorrelationId);
        Assert.Equal(correlationId, returned);
    }

    [Fact]
    public async Task TheAuditRowAndTheMessageCommitTogether()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var pingId = Guid.NewGuid();
        Guid correlationId;
        await using (var context = database.NewContext())
        {
            correlationId = await DataLayer(context).RecordPingRequestedAsync(pingId, Ct);
        }

        await using var reader = database.NewContext();
        var audit = await reader.AuditLogs.SingleAsync(Ct);
        Assert.Equal(OperationsAudit.PingRequested, audit.Action);
        Assert.Equal(pingId.ToString(), audit.ResourceId);
        Assert.Equal(correlationId, audit.CorrelationId);
        var message = await reader.OutboxMessages.SingleAsync(Ct);
        Assert.Equal(SystemPingRequested.MessageType, message.Type);
        Assert.Equal(correlationId, message.CorrelationId);
    }

    /// <summary>
    /// The atomicity guarantee (outbox.md, backend.md): the save's second INSERT fails after the first has already run
    /// inside the transaction, and the rollback takes both with it — no audit row, no message.
    /// </summary>
    [Fact]
    public async Task AFailedSaveLeavesNeitherTheAuditRowNorTheMessage()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var failSecondInsert = new FailNthInsert(2);

        await using (var context = database.NewContext(failSecondInsert))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => DataLayer(context).RecordPingRequestedAsync(Guid.NewGuid(), Ct));
        }

        // The first INSERT really executed before the failure, so this is a rollback, not a save that never started.
        Assert.Equal(1, failSecondInsert.Executed);
        await using var reader = database.NewContext();
        Assert.Equal(0, await reader.AuditLogs.CountAsync(Ct));
        Assert.Equal(0, await reader.OutboxMessages.CountAsync(Ct));
    }

    [Fact]
    public async Task AHandledPingIsRecordedAndThenFound()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var pingId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await using (var context = database.NewContext())
        {
            Assert.False(await DataLayer(context).PingHandledRecordedAsync(pingId, Ct));
            await DataLayer(context).RecordPingHandledAsync(pingId, messageId, Ct);
        }

        await using var reader = database.NewContext();
        Assert.True(await DataLayer(reader).PingHandledRecordedAsync(pingId, Ct));
        Assert.False(await DataLayer(reader).PingHandledRecordedAsync(Guid.NewGuid(), Ct));
        var row = await reader.AuditLogs.SingleAsync(Ct);
        Assert.Equal(OperationsAudit.PingHandled, row.Action);
        Assert.Equal($"outbox:{messageId}", row.AfterReference);
    }

    [Fact]
    public async Task ARequestedPingDoesNotCountAsHandled()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var pingId = Guid.NewGuid();
        await using (var context = database.NewContext())
        {
            await DataLayer(context).RecordPingRequestedAsync(pingId, Ct);
        }

        await using var handling = database.NewContext();
        Assert.False(await DataLayer(handling).PingHandledRecordedAsync(pingId, Ct));
    }

    private static OperationsDataLayer DataLayer(ArtsDbContext context)
    {
        var clock = new SystemClock(new FakeTimeProvider(Now));
        return new OperationsDataLayer(
            new OperationsRepository(context), new AuditWriter(context, clock), new OutboxWriter(context, clock), new CorrelationContext());
    }

    private sealed record Staged(string Type, string PayloadJson, Guid CorrelationId);

    private sealed class RecordingOutbox(List<string> steps) : IOutboxWriter
    {
        public List<Staged> Staged { get; } = [];

        public void Enqueue(string messageType, string payloadJson, Guid correlationId)
        {
            steps.Add("enqueue");
            Staged.Add(new Staged(messageType, payloadJson, correlationId));
        }
    }

    private sealed class RecordingAudit(List<string> steps) : IAuditWriter
    {
        public void Record(AuditEntry entry) => steps.Add("audit");
    }

    private sealed class RecordingRepository(List<string> steps) : IOperationsRepository
    {
        public Task<bool> PingHandledExistsAsync(Guid pingId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            steps.Add("save");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedCorrelation(Guid id) : ICorrelationContext
    {
        public Guid Current => id;
    }

    /// <summary>Lets the first n-1 INSERT commands run, then fails the nth the way the database would.</summary>
    private sealed class FailNthInsert(int n) : DbCommandInterceptor
    {
        private int inserts;

        public int Executed { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        private void Check(DbCommand command)
        {
            if (!command.CommandText.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (++inserts == n)
            {
                throw new InvalidOperationException("Simulated failure of an INSERT inside the save.");
            }

            Executed++;
        }
    }
}
