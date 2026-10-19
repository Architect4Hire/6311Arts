using System.Data.Common;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Identity.Data;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Data.Repositories;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Tests.Kernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Email;

/// <summary>
/// The outbox's central guarantee for the email path (outbox.md, add-outbox-message): the audit row and the
/// <c>email.send-requested</c> message commit together, or neither does.
/// </summary>
/// <remarks>
/// Against a real database rather than a mocked repository, because the thing under test is the transaction —
/// a mock would only prove that the calls happened in the order the test expected.
/// </remarks>
public sealed class EmailRequestAtomicityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheAuditRowAndTheMessageCommitTogether()
    {
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);

        await using (var context = database.NewContext())
        {
            await DataLayer(context).RequestEmailAsync(
                "user-1", IdentityEmailKinds.PasswordReset, IdentityAudit.PasswordResetRequested, "Requested.", Ct);
        }

        await using var reader = database.NewContext();
        var audit = await reader.AuditLogs.SingleAsync(Ct);
        var message = await reader.OutboxMessages.SingleAsync(Ct);

        Assert.Equal(IdentityAudit.PasswordResetRequested, audit.Action);
        Assert.Equal(EmailSendRequested.MessageType, message.Type);

        // Same correlation id, so one trace runs from the request through to the email (ADR 0019).
        Assert.Equal(audit.CorrelationId, message.CorrelationId);
    }

    [Fact]
    public async Task AFailedSaveLeavesNeitherTheAuditRowNorTheMessage()
    {
        // The failure that matters: half a commit would either audit a reset nobody was emailed about, or email
        // somebody with no record of why.
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);
        var failOnSecondInsert = new FailNthInsert(2);

        await using (var context = database.NewContext(failOnSecondInsert))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => DataLayer(context).RequestEmailAsync(
                "user-1", IdentityEmailKinds.PasswordReset, IdentityAudit.PasswordResetRequested, "Requested.", Ct));
        }

        Assert.True(failOnSecondInsert.Fired, "The interceptor never fired, so the save was not actually broken.");

        await using var reader = database.NewContext();
        Assert.Empty(await reader.AuditLogs.ToListAsync(Ct));
        Assert.Empty(await reader.OutboxMessages.ToListAsync(Ct));
    }

    [Fact]
    public async Task ThePayloadIsIdsOnly()
    {
        // No address, no token, no body (add-outbox-message). The composer resolves the recipient at send time.
        await using var database = await SqliteArtsDatabase.CreateAsync(Ct);

        await using (var context = database.NewContext())
        {
            await DataLayer(context).RequestEmailAsync(
                "user-1", IdentityEmailKinds.PasswordReset, IdentityAudit.PasswordResetRequested, "Requested.", Ct);
        }

        await using var reader = database.NewContext();
        var message = await reader.OutboxMessages.SingleAsync(Ct);
        var parsed = EmailSendRequested.TryParse(message.PayloadJson);

        Assert.Equal(new EmailSendRequested(IdentityEmailKinds.PasswordReset, "user-1"), parsed);
        Assert.DoesNotContain("@", message.PayloadJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// The real DataLayer with a <c>UserManager</c> over a database that has no tables.
    /// </summary>
    /// <remarks>
    /// That is the assertion, not a shortcut: the email path makes no Identity write at all, which is exactly
    /// what lets it be a single save. If it ever started touching <c>UserManager</c>, this store would throw and
    /// these tests would fail rather than quietly becoming two saves.
    /// </remarks>
    private static IdentityDataLayer DataLayer(ArtsDbContext context)
    {
        var clock = new SystemClock(new FakeTimeProvider(Now));

        return new IdentityDataLayer(
            new IdentityRepository(context),
            StubUserManager(),
            new AuditWriter(context, clock),
            new OutboxWriter(context, clock),
            new CorrelationContext());
    }

    private static UserManager<ArtsUser> StubUserManager() => new(
        new UserStore<ArtsUser>(new ArtsDbContext(new DbContextOptionsBuilder<ArtsDbContext>()
            .UseSqlite("DataSource=:memory:").Options)),
        optionsAccessor: null!,
        new PasswordHasher<ArtsUser>(),
        userValidators: [],
        passwordValidators: [],
        new UpperInvariantLookupNormalizer(),
        new IdentityErrorDescriber(),
        services: null!,
        logger: null!);

    private sealed class FailNthInsert(int n) : DbCommandInterceptor
    {
        private int inserts;

        public bool Fired { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Check(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
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
                Fired = true;
                throw new InvalidOperationException("Injected failure partway through the save.");
            }
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
