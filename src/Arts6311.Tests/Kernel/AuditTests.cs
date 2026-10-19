using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Kernel;

public sealed class AuditTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordStagesTheRowWithoutSaving()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using var context = database.NewContext();
        var writer = new AuditWriter(context, Clock());

        writer.Record(Entry());

        Assert.Single(context.ChangeTracker.Entries<AuditLog>(), entry => entry.State == EntityState.Added);
        await using var other = database.NewContext();
        Assert.Equal(0, await other.AuditLogs.CountAsync(ct));
    }

    [Fact]
    public async Task TheRowCommitsWithTheCallersSave()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using (var context = database.NewContext())
        {
            new AuditWriter(context, Clock()).Record(Entry());
            await context.SaveChangesAsync(ct);
        }

        await using var reader = database.NewContext();
        var row = await reader.AuditLogs.SingleAsync(ct);
        Assert.NotEqual(Guid.Empty, row.Id);
        Assert.Equal("artist.archived", row.Action);
        Assert.Equal(Now, row.OccurredAt);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheDomainWriteNorTheAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        Guid existingId;
        await using (var seed = database.NewContext())
        {
            new AuditWriter(seed, Clock()).Record(Entry());
            await seed.SaveChangesAsync(ct);
            existingId = (await seed.AuditLogs.SingleAsync(ct)).Id;
        }

        await using (var context = database.NewContext())
        {
            new AuditWriter(context, Clock()).Record(Entry() with { Action = "artist.deleted" });

            // Stands in for the caller's own domain write failing in the same save: a duplicate primary key the
            // database rejects, so the whole SaveChanges transaction rolls back.
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
        var actions = await reader.AuditLogs.Select(log => log.Action).ToListAsync(ct);
        Assert.Equal(["artist.archived"], actions);
    }

    [Theory]
    [InlineData("", "artist", "jane-doe", "Summary.")]
    [InlineData("artist.archived", " ", "jane-doe", "Summary.")]
    [InlineData("artist.archived", "artist", "", "Summary.")]
    [InlineData("artist.archived", "artist", "jane-doe", "")]
    public async Task MissingRequiredFieldsAreRejectedBeforeStaging(string action, string resourceType, string resourceId, string summary)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using var context = database.NewContext();
        var entry = new AuditEntry("user-1", action, resourceType, resourceId, Guid.NewGuid(), summary);

        Assert.Throws<ArgumentException>(() => new AuditWriter(context, Clock()).Record(entry));
        Assert.Empty(context.ChangeTracker.Entries<AuditLog>());
    }

    [Fact]
    public async Task OverLongValuesAreRejectedBeforeStaging()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using var context = database.NewContext();
        var writer = new AuditWriter(context, Clock());

        Assert.Throws<ArgumentException>(() => writer.Record(Entry() with { Summary = new string('x', AuditPolicy.SummaryMaxLength + 1) }));
        Assert.Throws<ArgumentException>(() => writer.Record(Entry() with { AfterReference = new string('x', AuditPolicy.ReferenceMaxLength + 1) }));
        Assert.Empty(context.ChangeTracker.Entries<AuditLog>());

        writer.Record(Entry() with { Summary = new string('x', AuditPolicy.SummaryMaxLength) });
        Assert.Single(context.ChangeTracker.Entries<AuditLog>());
    }

    [Fact]
    public async Task AnAuditRowCannotBeModified()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using var context = database.NewContext();
        new AuditWriter(context, Clock()).Record(Entry());
        await context.SaveChangesAsync(ct);

        var row = await context.AuditLogs.SingleAsync(ct);
        row.Summary = "rewritten";

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task AnAuditRowCannotBeDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqliteArtsDatabase.CreateAsync(ct);
        await using var context = database.NewContext();
        new AuditWriter(context, Clock()).Record(Entry());
        await context.SaveChangesAsync(ct);

        context.AuditLogs.Remove(await context.AuditLogs.SingleAsync(ct));

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(ct));
    }

    [Fact]
    public void TheSystemClockReadsTheTimeProvider()
    {
        Assert.Equal(Now, Clock().UtcNow);
    }

    private static SystemClock Clock() => new(new FakeTimeProvider(Now));

    private static AuditEntry Entry() =>
        new("user-1", "artist.archived", "artist", "jane-doe", Guid.NewGuid(), "Archived by an editor.");
}
