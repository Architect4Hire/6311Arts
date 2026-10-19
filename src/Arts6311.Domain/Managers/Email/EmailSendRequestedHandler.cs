using System.Security.Cryptography;
using System.Text;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Managers.Email;

/// <summary>
/// Delivers every <c>email.send-requested</c> message, whichever module wrote it.
/// </summary>
/// <remarks>
/// <para>
/// One handler for all kinds: it resolves the <see cref="IEmailComposer"/> keyed under the message's kind, so the
/// kernel never references a module and a new email type is a composer plus a registration.
/// </para>
/// <para>
/// <strong>Delivery is at-least-once, not exactly-once.</strong> The order is check the ledger, send, then record.
/// A crash between SMTP accepting the message and the ledger write redelivers and sends a second copy, and so
/// does a lease expiring under a slow relay while the first run is still in flight — both runs see no ledger row
/// and both send, because the unique index protects the <em>ledger</em>, not the mailbox. Recording first would
/// instead lose the email whenever the send failed, which is the worse failure: a duplicate password reset is a
/// nuisance, a missing one locks someone out. outbox.md already states at-least-once; the ledger narrows the
/// window, it does not close it (ADR 0023).
/// </para>
/// </remarks>
internal sealed partial class EmailSendRequestedHandler(
    IServiceProvider services,
    IEmailSender sender,
    ArtsDbContext context,
    IAuditWriter audit,
    IClock clock,
    ILogger<EmailSendRequestedHandler> logger) : IOutboxMessageHandler
{
    public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var request = EmailSendRequested.TryParse(message.PayloadJson)
            ?? throw new OutboxPoisonException("Malformed email.send-requested payload.");

        // An unknown kind is poison, not a retry: no composer is going to appear while this message waits.
        var composer = services.GetKeyedService<IEmailComposer>(request.Kind)
            ?? throw new OutboxPoisonException($"No email composer is registered for kind '{request.Kind}'.");

        bool alreadyHandled;
        try
        {
            alreadyHandled = await context.EmailDeliveries
                .AnyAsync(delivery => delivery.OutboxMessageId == message.Id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Wrapped like the others: a SqlException's message names the server, and the dispatcher writes what
            // it is given straight into OutboxMessage.LastError, which every Admin can read (outbox.md).
            throw new InvalidOperationException(
                $"Reading the delivery ledger for '{request.Kind}' failed ({exception.GetType().Name}).");
        }

        if (alreadyHandled)
        {
            // A redelivery of a message already handled. Logged at debug: it is normal, not a problem.
            LogAlreadyHandled(request.Kind, message.Id);
            return;
        }

        EmailMessage? email;
        try
        {
            email = await composer.ComposeAsync(request.SubjectId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            and not OutboxPoisonException)
        {
            // Only the type. A composer touches the user record and mints tokens, so its message could carry an
            // address or a token, and the dispatcher writes what it is given into LastError (outbox.md).
            throw new InvalidOperationException(
                $"Composing '{request.Kind}' failed ({exception.GetType().Name}).");
        }

        if (email is null)
        {
            // Nothing left to send — the subject is gone, or the thing it was about already happened. Recorded
            // rather than silently dropped, so a missing email can be explained without reading the logs.
            await RecordAsync(message, request, recipientHash: null, sent: false, cancellationToken);
            LogNothingToSend(request.Kind, request.SubjectId);
            return;
        }

        try
        {
            await sender.SendAsync(email, cancellationToken);
        }
        catch (EmailPermanentlyUndeliverableException exception)
        {
            // Will never succeed, so it is poisoned now rather than after five attempts. The audit row goes in
            // first, because this is the last anybody will hear of this message.
            LogSendFailed(request.Kind, exception.GetType().Name, message.Attempt);
            await RecordFinalFailureAsync(request, message, cancellationToken);

            throw new OutboxPoisonException($"'{request.Kind}' is permanently undeliverable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Logged with the type and the attempt number before it is sanitized away. The exception itself is
            // never logged: a MailKit message carries the host and port, and an auth failure could carry more.
            // Without this line a repeated failure says nothing about itself anywhere — which is exactly how the
            // SMTP connection-string bug in this prompt cost five silent attempts (ADR 0023).
            LogSendFailed(request.Kind, exception.GetType().Name, message.Attempt);

            // outbox.md: on the last attempt a handler that calls a provider records a state a person can see.
            // There is no Sync screen until 1.12, so the visible state is an audit row plus the Poisoned message.
            // No ledger row is written — that would make an Admin's replay a silent no-op.
            if (message.Attempt >= OutboxPolicy.MaxAttempts)
            {
                await RecordFinalFailureAsync(request, message, cancellationToken);
            }

            throw new InvalidOperationException(
                $"Sending '{request.Kind}' failed ({exception.GetType().Name}).");
        }

        await RecordAsync(message, request, HashRecipient(email.To), sent: true, cancellationToken);
        LogSent(request.Kind, request.SubjectId);
    }

    private async Task RecordAsync(
        OutboxMessageEnvelope message,
        EmailSendRequested request,
        string? recipientHash,
        bool sent,
        CancellationToken cancellationToken)
    {
        context.EmailDeliveries.Add(new EmailDelivery
        {
            OutboxMessageId = message.Id,
            Kind = request.Kind,
            SubjectId = request.SubjectId,
            RecipientHash = recipientHash,
            HandledAt = clock.UtcNow,
            Sent = sent,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Most likely the unique index on OutboxMessageId: another delivery recorded first, and the email is
            // sent either way, so this is a race somebody else won rather than a failure to report. But it could
            // equally be a timeout or a truncation, so the row is re-read instead of assumed — otherwise a
            // genuinely lost ledger entry would be logged as the benign case and never noticed.
            context.ChangeTracker.Clear();

            if (await context.EmailDeliveries
                .AnyAsync(delivery => delivery.OutboxMessageId == message.Id, cancellationToken))
            {
                LogAlreadyHandled(request.Kind, message.Id);
                return;
            }

            // Not rethrown: the email has already gone, and retrying would send a second copy. Logged loudly
            // because the ledger no longer protects this message from a redelivery.
            LogDeliveryNotRecorded(request.Kind, message.Id);
        }
    }

    /// <summary>
    /// Records, for somebody to find, that this email will not be sent.
    /// </summary>
    /// <remarks>
    /// Written on its own save and deliberately not inside the delivery ledger: an Admin replaying the poisoned
    /// message must get a real attempt, not a no-op.
    /// </remarks>
    private async Task RecordFinalFailureAsync(
        EmailSendRequested request, OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        try
        {
            audit.Record(new AuditEntry(
                ActorUserId: null,
                "email.delivery-failed",
                "email.delivery",
                request.SubjectId,
                message.CorrelationId,
                $"Giving up on '{request.Kind}' after {message.Attempt} attempts.",
                AfterReference: $"outbox:{message.Id}"));

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Best effort. The send has already failed and is about to be reported; losing the audit row must not
            // replace that failure with a different one.
            LogFinalFailureNotRecorded(request.Kind, exception.GetType().Name);
        }
    }

    /// <summary>
    /// SHA-256 of the lower-cased address.
    /// </summary>
    /// <remarks>
    /// A correlation aid for support — "did we send to this person?" — not a privacy measure: an unsalted hash of
    /// an address is reversible by dictionary against any address somebody already has. It exists so the ledger
    /// need not hold a second plaintext copy of what the user table already stores.
    /// </remarks>
    private static string HashRecipient(string address) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.ToLowerInvariant())));

    [LoggerMessage(Level = LogLevel.Debug, Message = "Email {Kind} for message {OutboxMessageId} was already handled.")]
    private partial void LogAlreadyHandled(string kind, Guid outboxMessageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {Kind} for {SubjectId} had nothing to send.")]
    private partial void LogNothingToSend(string kind, string subjectId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email {Kind} sent for {SubjectId}.")]
    private partial void LogSent(string kind, string subjectId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Email {Kind} failed to send ({ExceptionType}) on attempt {Attempt}.")]
    private partial void LogSendFailed(string kind, string exceptionType, int attempt);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Email {Kind} for message {OutboxMessageId} was sent but not recorded; a redelivery would "
            + "send it again.")]
    private partial void LogDeliveryNotRecorded(string kind, Guid outboxMessageId);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not record the final failure of email {Kind} ({ExceptionType}).")]
    private partial void LogFinalFailureNotRecorded(string kind, string exceptionType);
}
