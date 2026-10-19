using System.Security.Cryptography;
using System.Text;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Idempotency;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// The idempotency seam over a SQLite file database with a connection per scope, like requests, so two requests
/// genuinely contend for the scope's unique index.
/// </summary>
public sealed class IdempotencyTests : IAsyncLifetime
{
    private const string Operation = "test.create-thing";
    private const string Subject = "user:3f0c9d6e-6b1a-4c55-9a8e-1f2d3c4b5a69";

    private static readonly string FingerprintKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
    private SqliteArtsDatabase database = null!;
    private ServiceProvider services = null!;
    private int runs;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        database = await SqliteArtsDatabase.CreateFileAsync(Ct);
        services = BuildServices(FingerprintKey);
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task TheFirstRequestRunsOnceAndCommitsItsWritesWithTheRecord()
    {
        var outcome = await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"));

        Assert.True(outcome.Result.Succeeded);
        Assert.False(outcome.Replayed);
        Assert.Equal(new Created("Bakers", 1), outcome.Result.Value);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task AReplayReturnsTheOriginalResultWithoutRunningAgain()
    {
        var first = await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"));
        var replay = await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"));

        Assert.True(replay.Replayed);
        Assert.Equal(first.Result.Value, replay.Result.Value);
        Assert.Equal(1, runs);

        // The operation's domain write and its outbox message happened once, not once per request.
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task TheSameKeyWithADifferentBodyIsRefused()
    {
        await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"));

        var mismatch = await ExecuteAsync(Command("key-1", new { name = "Potters" }), Create("Potters"));

        Assert.Equal(ErrorCodes.IdempotencyKeyReused, mismatch.Result.Error?.Code);
        Assert.False(mismatch.Replayed);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task ConcurrentFirstUseOfAKeyRunsTheOperationExactlyOnce()
    {
        var command = Command("key-1", new { name = "Bakers" });

        var outcomes = await Task.WhenAll(
            Task.Run(() => ExecuteAsync(command, Create("Bakers", delay: TimeSpan.FromMilliseconds(300))), Ct),
            Task.Run(() => ExecuteAsync(command, Create("Bakers", delay: TimeSpan.FromMilliseconds(300))), Ct));

        Assert.Equal(1, runs);
        Assert.All(outcomes, outcome => Assert.True(outcome.Result.Succeeded));
        Assert.Single(outcomes, outcome => outcome.Replayed);
        Assert.Equal(outcomes[0].Result.Value, outcomes[1].Result.Value);
        Assert.Equal((1, 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task AfterRetentionTheSameKeyRunsFresh()
    {
        await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"));
        time.Advance(IdempotencyPolicy.Retention - TimeSpan.FromSeconds(1));
        Assert.True((await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"))).Replayed);

        time.Advance(TimeSpan.FromSeconds(1));
        var fresh = await ExecuteAsync(Command("key-1", new { name = "Potters" }), Create("Potters"));

        Assert.False(fresh.Replayed);
        Assert.Equal(new Created("Potters", 2), fresh.Result.Value);

        // The expired record was replaced, not joined.
        var (records, _, _) = await CountsAsync();
        Assert.Equal(1, records);
    }

    [Fact]
    public async Task AMissingKeyIsRefusedWhenRequired()
    {
        var outcome = await ExecuteAsync(Command(null, new { name = "Bakers" }), Create("Bakers"));

        Assert.Equal(ErrorCodes.IdempotencyKeyRequired, outcome.Result.Error?.Code);
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task AMissingKeyRunsUnprotectedWhenOptional()
    {
        var command = Command(null, new { name = "Bakers" }) with { KeyRequired = false };

        await ExecuteAsync(command, Create("Bakers"));
        var second = await ExecuteAsync(command, Create("Bakers"));

        Assert.False(second.Replayed);
        Assert.Equal(2, runs);
        Assert.Equal((0, 2, 2), await CountsAsync());
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("tab\there")]
    [InlineData("café")]
    [InlineData("\u0001")]
    public async Task AKeyThatIsNotPrintableAsciiIsRefused(string key)
    {
        var outcome = await ExecuteAsync(Command(key, new { name = "Bakers" }), Create("Bakers"));

        Assert.Equal(ErrorCodes.IdempotencyKeyInvalid, outcome.Result.Error?.Code);
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task AKeyOfExactlyTheMaximumLengthIsAccepted()
    {
        var outcome = await ExecuteAsync(
            Command(new string('k', IdempotencyPolicy.KeyMaxLength), new { name = "Bakers" }), Create("Bakers"));

        Assert.True(outcome.Result.Succeeded);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task AnOverLongKeyIsRefused()
    {
        var outcome = await ExecuteAsync(
            Command(new string('k', IdempotencyPolicy.KeyMaxLength + 1), new { name = "Bakers" }), Create("Bakers"));

        Assert.Equal(ErrorCodes.IdempotencyKeyInvalid, outcome.Result.Error?.Code);
    }

    [Fact]
    public async Task AFailedResultIsNotRememberedAndItsWritesRollBack()
    {
        var failed = await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers", fail: true));
        Assert.Equal("test.failed", failed.Result.Error?.Code);
        Assert.Equal((0, 0, 0), await CountsAsync());

        var retry = await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"));

        Assert.True(retry.Result.Succeeded);
        Assert.False(retry.Replayed);
        Assert.Equal(2, runs);
    }

    [Fact]
    public async Task KeysAreScopedToTheirSubjectAndOperation()
    {
        await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"));

        var otherSubject = await ExecuteAsync(
            Command("key-1", new { name = "Bakers" }) with { Subject = "cart:9b1d" }, Create("Bakers"));
        var otherOperation = await ExecuteAsync(
            Command("key-1", new { name = "Bakers" }) with { Operation = "test.other-thing" }, Create("Bakers"));

        // Neither may see the first caller's result.
        Assert.False(otherSubject.Replayed);
        Assert.False(otherOperation.Replayed);
        Assert.Equal(3, runs);
    }

    [Fact]
    public async Task PropertyOrderDoesNotMakeADifferentRequest()
    {
        await ExecuteAsync(
            Command("key-1", new Dictionary<string, object> { ["name"] = "Bakers", ["size"] = 3 }), Create("Bakers"));

        var reordered = await ExecuteAsync(
            Command("key-1", new Dictionary<string, object> { ["size"] = 3, ["name"] = "Bakers" }), Create("Bakers"));

        Assert.True(reordered.Replayed);
    }

    [Fact]
    public async Task TheStoredRowHoldsAKeyedHashAndNoPayloadPlaintext()
    {
        const string marker = "sensitive-marker-value";
        await ExecuteAsync(Command("key-1", new { name = "Bakers", note = marker }), Create("Bakers"));

        await using var reader = database.NewContext();
        var row = await reader.IdempotencyRecords.SingleAsync(Ct);

        Assert.Equal(32, row.FingerprintHash.Length);
        Assert.DoesNotContain(marker, row.ResultJson + row.Key + row.Operation + row.Subject, StringComparison.Ordinal);
        var unkeyed = SHA256.HashData(Encoding.UTF8.GetBytes(Operation + "\n" + """{"name":"Bakers","note":"sensitive-marker-value"}"""));
        Assert.NotEqual(unkeyed, row.FingerprintHash); // without the server key, guesses cannot be tested
        Assert.Equal(time.GetUtcNow() + IdempotencyPolicy.Retention, row.ExpiresAt);
    }

    [Fact]
    public async Task ADifferentServerKeyProducesADifferentFingerprint()
    {
        await ExecuteAsync(Command("key-1", new { name = "Bakers" }), Create("Bakers"));

        await using var other = BuildServices(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        await using var scope = other.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<IIdempotentCommandExecutor>().ExecuteAsync(
            Command("key-1", new { name = "Bakers" }), Create("Bakers")(scope.ServiceProvider), Ct);

        Assert.Equal(ErrorCodes.IdempotencyKeyReused, outcome.Result.Error?.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("user: spaced")]
    [InlineData("user:café")]
    public async Task ABadSubjectIsTheFacadesBugAndThrows(string subject)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => ExecuteAsync(Command("key-1", new { }) with { Subject = subject }, Create("Bakers")));
        Assert.Equal(0, runs);
    }

    [Fact]
    public async Task AnOverLongOperationNameThrows()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ExecuteAsync(
            Command("key-1", new { }) with { Operation = new string('o', IdempotencyPolicy.OperationMaxLength + 1) },
            Create("Bakers")));
    }

    [Fact]
    public async Task ABinaryResultCannotBeStored()
    {
        await using var scope = services.CreateAsyncScope();
        var executor = scope.ServiceProvider.GetRequiredService<IIdempotentCommandExecutor>();

        await Assert.ThrowsAsync<NotSupportedException>(() => executor.ExecuteAsync(
            Command("key-1", new { }), _ => Task.FromResult(OperationResult.Success<byte[]>([1, 2])), Ct));
    }

    private ServiceProvider BuildServices(string fingerprintKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Idempotency:FingerprintKey"] = fingerprintKey })
            .Build();

        return new ServiceCollection()
            .AddSingleton<TimeProvider>(time)
            .AddScoped(_ => database.NewContext())
            .AddArtsKernel()
            .AddArtsIdempotency(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    private static IdempotentCommand Command(string? key, object fingerprint) => new(Subject, Operation, key, fingerprint);

    /// <summary>
    /// Stands in for a facade's one Business call: a domain write (an audit row) and an outbox message, saved once
    /// through the request's DbContext, exactly as a DataLayer would.
    /// </summary>
    private Func<IServiceProvider, Func<CancellationToken, Task<OperationResult<Created>>>> Create(
        string name, TimeSpan? delay = null, bool fail = false) => requestServices => async cancellationToken =>
    {
        var run = Interlocked.Increment(ref runs);
        requestServices.GetRequiredService<IAuditWriter>().Record(
            new AuditEntry("user-1", "thing.created", "thing", name, Guid.NewGuid(), "Created a thing."));
        requestServices.GetRequiredService<IOutboxWriter>().Enqueue("test.thing-created", "{}", Guid.NewGuid());
        await requestServices.GetRequiredService<ArtsDbContext>().SaveChangesAsync(cancellationToken);

        if (delay is { } wait)
        {
            await Task.Delay(wait, cancellationToken);
        }

        return fail
            ? OperationResult.Failure<Created>(OperationError.Of("test.failed", "The operation failed."))
            : OperationResult.Success(new Created(name, run));
    };

    private async Task<IdempotentOutcome<Created>> ExecuteAsync(
        IdempotentCommand command, Func<IServiceProvider, Func<CancellationToken, Task<OperationResult<Created>>>> operation)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIdempotentCommandExecutor>()
            .ExecuteAsync(command, operation(scope.ServiceProvider), Ct);
    }

    private async Task<(int Records, int DomainRows, int Messages)> CountsAsync()
    {
        await using var reader = database.NewContext();
        return (await reader.IdempotencyRecords.CountAsync(Ct),
            await reader.AuditLogs.CountAsync(Ct),
            await reader.OutboxMessages.CountAsync(Ct));
    }

    private sealed record Created(string Name, int Run);
}

public sealed class IdempotencyOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("c2hvcnQ=")] // "short": 5 bytes
    public void AKeyThatIsNotBase64OfAtLeast32BytesIsInvalid(string? key)
    {
        Assert.False(IdempotencyOptions.IsValidKey(key));
    }

    [Fact]
    public void ThirtyTwoBytesIsValid()
    {
        Assert.True(IdempotencyOptions.IsValidKey(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
    }

    [Fact]
    public void AHostWithoutAValidKeyCannotResolveTheOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Idempotency:FingerprintKey"] = "c2hvcnQ=" })
            .Build();
        using var services = new ServiceCollection().AddArtsIdempotency(configuration).BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<IdempotencyOptions>>().Value);
    }

    [Fact]
    public async Task AHostWithoutAValidKeyFailsToStart()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Idempotency:FingerprintKey"] = "c2hvcnQ=" });
        builder.Services.AddArtsIdempotency(builder.Configuration);
        using var host = builder.Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The scope columns compare byte-for-byte on SQL Server, where the database default is case-insensitive. SQLite
    /// compares binary already, so the SQLite tests cannot see this; the SQL Server model is checked instead.
    /// </summary>
    [Fact]
    public void TheScopeIsCaseSensitiveOnSqlServer()
    {
        var options = new DbContextOptionsBuilder<ArtsDbContext>().UseSqlServer();
        ArtsDbContextOptions.Configure(options);
        using var context = new ArtsDbContext(options.Options);
        // Collations live in the design-time model (what migrations are generated from), not the runtime one.
        var record = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IdempotencyRecord))!;

        Assert.All(
            [nameof(IdempotencyRecord.Subject), nameof(IdempotencyRecord.Operation), nameof(IdempotencyRecord.Key)],
            name => Assert.Equal("Latin1_General_BIN2", record.FindProperty(name)!.GetCollation()));
    }

    [Fact]
    public void TheWorkersRegistrationNeedsNoKey()
    {
        var services = new ServiceCollection().AddArtsIdempotencyMaintenance();

        Assert.Contains(services, d => d.ServiceType == typeof(IIdempotencyMaintenance));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IIdempotentCommandExecutor));
        Assert.DoesNotContain(services, d => d.ServiceType.Name.Contains("IdempotencyOptions", StringComparison.Ordinal));
    }
}
