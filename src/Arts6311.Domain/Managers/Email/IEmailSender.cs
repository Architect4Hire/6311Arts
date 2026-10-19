namespace Arts6311.Domain.Managers.Email;

/// <summary>
/// Sends one email. The provider boundary (external.md): declared here, implemented by the Worker over SMTP for
/// Mailpit locally, and by Azure Communication Services when published (6.1).
/// </summary>
/// <remarks>
/// Only the Worker implements and resolves this. Every email in the system is an outbox message, so no request
/// path ever sends one — which is also why no other host carries an SMTP client (ADR 0023).
/// </remarks>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// A send that will never succeed however often it is retried: an address the server rejects outright, or a
/// permanent 5xx refusal.
/// </summary>
/// <remarks>
/// Separate from an ordinary failure so the handler can poison it on the first attempt rather than spending five.
/// A sender raises it only for an outcome it knows is final; anything ambiguous is a plain exception and retries.
/// </remarks>
public sealed class EmailPermanentlyUndeliverableException : Exception
{
    public EmailPermanentlyUndeliverableException(string message) : base(message)
    {
    }

    public EmailPermanentlyUndeliverableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public EmailPermanentlyUndeliverableException()
    {
    }
}
