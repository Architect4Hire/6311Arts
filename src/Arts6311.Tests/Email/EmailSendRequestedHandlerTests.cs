using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers.ViewModels;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Tests.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Email;

/// <summary>
/// The one handler for <c>email.send-requested</c>, whichever module wrote the message (outbox.md,
/// add-notification).
/// </summary>
public sealed class EmailSendRequestedHandlerTests
{
    private const string Correct = "a-perfectly-fine-passphrase";

    [Fact]
    public async Task APasswordResetIsSentOnceAndCarriesALinkToTheConfiguredOrigin()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        await HandleAsync(host, Message(IdentityEmailKinds.PasswordReset, user.Id));

        var sent = Assert.Single(host.Email.Sent);
        Assert.Equal("m@6311arts.test", sent.To);
        Assert.Contains("https://6311arts.test/account/reset-password", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("userId=", sent.TextBody, StringComparison.Ordinal);
        Assert.Contains("token=", sent.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADuplicateDeliverySendsOnlyOnce()
    {
        // Delivery is at-least-once: the dispatcher redelivers after a crash between the handler finishing and
        // the outcome being recorded. The ledger's unique index on the message id is what makes the second
        // delivery a no-op (ADR 0023).
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        var message = Message(IdentityEmailKinds.PasswordReset, user.Id);

        await HandleAsync(host, message);
        await HandleAsync(host, message);

        Assert.Single(host.Email.Sent);
    }

    [Fact]
    public async Task ASecondRequestForTheSamePersonDoesSendAgain()
    {
        // The reason the ledger keys on the outbox message id rather than on (kind, subject): somebody may ask
        // for a second reset, and keying on the subject would swallow it silently (ADR 0023).
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        await HandleAsync(host, Message(IdentityEmailKinds.PasswordReset, user.Id));
        await HandleAsync(host, Message(IdentityEmailKinds.PasswordReset, user.Id));

        Assert.Equal(2, host.Email.Sent.Count);
    }

    [Fact]
    public async Task AConfirmationForAnAlreadyConfirmedAddressIsRecordedAsASkip()
    {
        // The composer returns nothing, which is a success, not a retry: by the time the Worker got to it the
        // thing the email was about had already happened.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        var message = Message(IdentityEmailKinds.EmailConfirmation, user.Id);

        await HandleAsync(host, message);

        Assert.Empty(host.Email.Sent);

        using var scope = host.CreateScope();
        var delivery = await IdentityTestHost.Resolve<ArtsDbContext>(scope).EmailDeliveries
            .SingleAsync(row => row.OutboxMessageId == message.Id, Ct);

        Assert.False(delivery.Sent);
        Assert.Null(delivery.RecipientHash);
    }

    [Fact]
    public async Task AConfirmationForAnUnconfirmedAddressIsSent()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("new@6311arts.test", Correct, emailConfirmed: false, IdentityRoles.Member);

        await HandleAsync(host, Message(IdentityEmailKinds.EmailConfirmation, user.Id));

        var sent = Assert.Single(host.Email.Sent);
        Assert.Contains("/account/confirm-email", sent.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedPayloadIsPoisonedWithoutSending()
    {
        // No handler will ever make sense of it, so spending all five attempts is wasted work and a delay before
        // anyone notices (outbox.md).
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var message = new OutboxMessageEnvelope(
            Guid.NewGuid(), EmailSendRequested.MessageType, "{ not json", Guid.NewGuid(), Attempt: 1);

        await Assert.ThrowsAsync<OutboxPoisonException>(() => HandleAsync(host, message));

        Assert.Empty(host.Email.Sent);
    }

    [Fact]
    public async Task AnUnknownKindIsPoisoned()
    {
        // Same reasoning: no composer is going to appear while the message waits.
        await using var host = await IdentityTestHost.CreateAsync(Ct);

        await Assert.ThrowsAsync<OutboxPoisonException>(
            () => HandleAsync(host, Message("something.nobody-registered", "whoever")));

        Assert.Empty(host.Email.Sent);
    }

    [Fact]
    public async Task APayloadMissingAFieldIsPoisoned()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var message = new OutboxMessageEnvelope(
            Guid.NewGuid(), EmailSendRequested.MessageType, """{"kind":"identity.password-reset"}""",
            Guid.NewGuid(), Attempt: 1);

        await Assert.ThrowsAsync<OutboxPoisonException>(() => HandleAsync(host, message));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"kind":"identity.password-reset","subjectId":""}""")]
    [InlineData("""{"kind":"identity.password-reset","subjectId":"   "}""")]
    [InlineData("""{"kind":"","subjectId":"user-1"}""")]
    public async Task EveryMalformedShapeIsPoisonedWithoutSending(string payloadJson)
    {
        // The matrix the 1.5 ping's payload tests had, for the message type that replaced it on the outbox's
        // end-to-end path: anything TryParse cannot make sense of is poisoned on its first attempt.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var message = new OutboxMessageEnvelope(
            Guid.NewGuid(), EmailSendRequested.MessageType, payloadJson, Guid.NewGuid(), Attempt: 1);

        await Assert.ThrowsAsync<OutboxPoisonException>(() => HandleAsync(host, message));

        Assert.Empty(host.Email.Sent);
    }

    [Fact]
    public async Task ASendFailureNamesNoHostOrCredential()
    {
        // The dispatcher writes whatever it is given into OutboxMessage.LastError, which every Admin can read.
        // A MailKit failure message would carry the SMTP host and port (outbox.md).
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        host.Email.Throw = new InvalidOperationException("smtp://mail-host:1025 rejected AUTH for user hunter2");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => HandleAsync(host, Message(IdentityEmailKinds.PasswordReset, user.Id)));

        Assert.DoesNotContain("mail-host", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1025", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(InvalidOperationException), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedSendIsNotRecordedSoItCanBeRetried()
    {
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        var message = Message(IdentityEmailKinds.PasswordReset, user.Id);
        host.Email.Throw = new InvalidOperationException("the relay is down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(host, message));

        using (var scope = host.CreateScope())
        {
            Assert.False(await IdentityTestHost.Resolve<ArtsDbContext>(scope).EmailDeliveries
                .AnyAsync(row => row.OutboxMessageId == message.Id, Ct));
        }

        // And the retry succeeds once the relay is back.
        host.Email.Throw = null;
        await HandleAsync(host, message);

        Assert.Single(host.Email.Sent);
    }

    [Fact]
    public async Task TheLedgerHoldsNoAddress()
    {
        // Only a hash: enough to answer "did they get it?" in support without a second copy of everyone's email
        // beside the user table.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        var message = Message(IdentityEmailKinds.PasswordReset, user.Id);

        await HandleAsync(host, message);

        using var scope = host.CreateScope();
        var delivery = await IdentityTestHost.Resolve<ArtsDbContext>(scope).EmailDeliveries
            .SingleAsync(row => row.OutboxMessageId == message.Id, Ct);

        Assert.True(delivery.Sent);
        Assert.Matches("^[0-9a-f]{64}$", delivery.RecipientHash);
    }

    [Fact]
    public async Task TheResetTokenNeverTouchesThePayload()
    {
        // add-outbox-message forbids tokens in a payload, which is exactly why the composer mints one at send
        // time rather than the request staging it.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);

        using var scope = host.CreateScope();
        await IdentityTestHost.Resolve<IIdentityFacade>(scope).RequestPasswordResetAsync(new PasswordResetRequestViewModel("m@6311arts.test"), Ct);

        var staged = await IdentityTestHost.Resolve<ArtsDbContext>(scope).OutboxMessages
            .SingleAsync(row => row.Type == EmailSendRequested.MessageType, Ct);

        Assert.Equal(new EmailSendRequested(IdentityEmailKinds.PasswordReset, user.Id).Serialize(), staged.PayloadJson);
        Assert.DoesNotContain("token", staged.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheLastAttemptLeavesSomethingAPersonCanSee()
    {
        // outbox.md: a handler that calls a provider records a visible terminal state on its last attempt. There
        // is no Sync screen until 1.12, so the visible state is an audit row beside the Poisoned message.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        host.Email.Throw = new InvalidOperationException("the relay is down");

        var message = Message(IdentityEmailKinds.PasswordReset, user.Id) with { Attempt = OutboxPolicy.MaxAttempts };

        await Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync(host, message));

        using var scope = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);
        var entry = await context.AuditLogs.SingleAsync(log => log.Action == "email.delivery-failed", Ct);

        Assert.Equal(user.Id, entry.ResourceId);
        Assert.Equal($"outbox:{message.Id}", entry.AfterReference);

        // And no ledger row, so an Admin replaying the poisoned message gets a real attempt rather than a no-op.
        Assert.False(await context.EmailDeliveries.AnyAsync(row => row.OutboxMessageId == message.Id, Ct));
    }

    [Fact]
    public async Task AnEarlierAttemptRecordsNothingTerminal()
    {
        // The counterpart: attempt 1 of 5 must not announce a final failure.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        host.Email.Throw = new InvalidOperationException("the relay is down");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HandleAsync(host, Message(IdentityEmailKinds.PasswordReset, user.Id)));

        using var scope = host.CreateScope();
        Assert.False(await IdentityTestHost.Resolve<ArtsDbContext>(scope).AuditLogs
            .AnyAsync(log => log.Action == "email.delivery-failed", Ct));
    }

    [Fact]
    public async Task APermanentlyUndeliverableAddressIsPoisonedOnTheFirstAttempt()
    {
        // An address the server will never accept, or a 5xx refusal: retrying four more times only delays the
        // moment anybody notices.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        host.Email.Throw = new EmailPermanentlyUndeliverableException("no such mailbox");

        var message = Message(IdentityEmailKinds.PasswordReset, user.Id);

        await Assert.ThrowsAsync<OutboxPoisonException>(() => HandleAsync(host, message));

        using var scope = host.CreateScope();
        var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);

        // Visible immediately, on attempt 1, rather than after five.
        Assert.True(await context.AuditLogs.AnyAsync(log => log.Action == "email.delivery-failed", Ct));
    }

    [Fact]
    public async Task AnAlreadyRecordedDeliveryLosesTheRaceCleanly()
    {
        // The branch where two deliveries pass the ledger check and the unique index decides. Simulated by having
        // the send insert the row, so the handler's own record hits the constraint — which the sequential
        // duplicate test never reaches, because its ledger check short-circuits first.
        await using var host = await IdentityTestHost.CreateAsync(Ct);
        var user = await host.CreateUserAsync("m@6311arts.test", Correct, emailConfirmed: true, IdentityRoles.Member);
        var message = Message(IdentityEmailKinds.PasswordReset, user.Id);

        host.Email.OnSend = () =>
        {
            using var scope = host.CreateScope();
            var context = IdentityTestHost.Resolve<ArtsDbContext>(scope);
            context.EmailDeliveries.Add(new EmailDelivery
            {
                OutboxMessageId = message.Id,
                Kind = IdentityEmailKinds.PasswordReset,
                SubjectId = user.Id,
                HandledAt = host.Time.GetUtcNow(),
                Sent = true,
            });
            context.SaveChanges();
        };

        // No throw: the other delivery recorded it, the email is sent either way.
        await HandleAsync(host, message);

        using var reader = host.CreateScope();
        Assert.Equal(
            1,
            await IdentityTestHost.Resolve<ArtsDbContext>(reader).EmailDeliveries
                .CountAsync(row => row.OutboxMessageId == message.Id, Ct));
    }

    [Fact]
    public async Task AComposerFailureNamesOnlyItsType()
    {
        // A composer reads the user row and mints a token, so its exception message could carry an address or a
        // token — and the dispatcher writes what it is given into LastError, which every Admin can read.
        const string Kind = "test.throwing-composer";
        await using var host = await IdentityTestHost.CreateAsync(
            Ct,
            configureServices: services => services.AddKeyedScoped<IEmailComposer>(
                Kind, (_, _) => new ThrowingComposer()));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => HandleAsync(host, Message(Kind, "user-1")));

        Assert.DoesNotContain("someone@example.test", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CfDJ8-secret-token", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), failure.Message, StringComparison.Ordinal);
        Assert.Empty(host.Email.Sent);
    }

    private sealed class ThrowingComposer : IEmailComposer
    {
        public Task<EmailMessage?> ComposeAsync(string subjectId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Failed for someone@example.test with token CfDJ8-secret-token");
    }

    private static OutboxMessageEnvelope Message(string kind, string subjectId) => new(
        Guid.NewGuid(),
        EmailSendRequested.MessageType,
        new EmailSendRequested(kind, subjectId).Serialize(),
        Guid.NewGuid(),
        Attempt: 1);

    private static async Task HandleAsync(IdentityTestHost host, OutboxMessageEnvelope message)
    {
        using var scope = host.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredKeyedService<IOutboxMessageHandler>(
            EmailSendRequested.MessageType);

        await handler.HandleAsync(message, Ct);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
