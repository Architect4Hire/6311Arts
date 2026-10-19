using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Kernel;

public sealed class OutboxWriterTests
{
    private const string MessageType = "test.message-requested";

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnqueueStagesTheRowWithoutSaving()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using var context = database.NewContext();

        new OutboxWriter(context, Clock()).Enqueue(MessageType, "{}", Guid.NewGuid());

        var staged = Assert.Single(context.ChangeTracker.Entries<OutboxMessage>());
        Assert.Equal(EntityState.Added, staged.State);
        // The id exists before the save, so a DataLayer could already hand it on as an idempotency key.
        Assert.NotEqual(Guid.Empty, staged.Entity.Id);
        await using var other = database.NewContext();
        Assert.Equal(0, await other.OutboxMessages.CountAsync(ct));
    }

    [Fact]
    public async Task TheRowCommitsWithTheCallersSaveAsPendingAndDueNow()
    {
        var ct = TestContext.Current.CancellationToken;
        var correlationId = Guid.NewGuid();
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using (var context = database.NewContext())
        {
            new OutboxWriter(context, Clock()).Enqueue(MessageType, """{"id":"42"}""", correlationId);
            await context.SaveChangesAsync(ct);
        }

        await using var reader = database.NewContext();
        var row = await reader.OutboxMessages.SingleAsync(ct);
        Assert.Equal(MessageType, row.Type);
        Assert.Equal("""{"id":"42"}""", row.PayloadJson);
        Assert.Equal(correlationId, row.CorrelationId);
        Assert.Equal(OutboxMessageStatus.Pending, row.Status);
        Assert.Equal(Now, row.CreatedAt);
        Assert.Equal(Now, row.AvailableAt);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.LeasedBy);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheDomainWriteNorTheMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        Guid existingId;
        await using (var seed = database.NewContext())
        {
            new AuditWriter(seed, Clock()).Record(Entry("artist.archived"));
            await seed.SaveChangesAsync(ct);
            existingId = (await seed.AuditLogs.SingleAsync(ct)).Id;
        }

        await using (var context = database.NewContext())
        {
            // The domain write, its message, and a duplicate primary key the database rejects, all in one save:
            // the whole SaveChanges transaction rolls back.
            new AuditWriter(context, Clock()).Record(Entry("artist.deleted"));
            new OutboxWriter(context, Clock()).Enqueue(MessageType, "{}", Guid.NewGuid());
            context.AuditLogs.Add(new AuditLog
            {
                Id = existingId,
                Action = "duplicate",
                ResourceType = "artist",
                ResourceId = "jane-doe",
                Summary = "Fails on insert.",
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(ct));
        }

        await using var reader = database.NewContext();
        Assert.Equal(["artist.archived"], await reader.AuditLogs.Select(log => log.Action).ToListAsync(ct));
        Assert.Equal(0, await reader.OutboxMessages.CountAsync(ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingMessageTypeIsRejectedBeforeStaging(string messageType)
    {
        await AssertRejectedAsync(writer => writer.Enqueue(messageType, "{}", Guid.NewGuid()));
    }

    [Fact]
    public async Task AnOverLongMessageTypeIsRejectedBeforeStaging()
    {
        await AssertRejectedAsync(writer =>
            writer.Enqueue(new string('a', OutboxPolicy.TypeMaxLength + 1), "{}", Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AMissingPayloadIsRejectedBeforeStaging(string payloadJson)
    {
        await AssertRejectedAsync(writer => writer.Enqueue(MessageType, payloadJson, Guid.NewGuid()));
    }

    [Fact]
    public async Task AnEmptyCorrelationIdIsRejectedBeforeStaging()
    {
        await AssertRejectedAsync(writer => writer.Enqueue(MessageType, "{}", Guid.Empty));
    }

    private static async Task AssertRejectedAsync(Action<OutboxWriter> enqueue)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using var context = database.NewContext();

        Assert.Throws<ArgumentException>(() => enqueue(new OutboxWriter(context, Clock())));
        Assert.Empty(context.ChangeTracker.Entries<OutboxMessage>());
    }

    private static AuditEntry Entry(string action) =>
        new("user-1", action, "artist", "jane-doe", Guid.NewGuid(), "Archived the artist.");

    private static SystemClock Clock() => new(new FakeTimeProvider(Now));
}
