namespace Arts6311.Domain.Managers.Outbox;

/// <summary>
/// Thrown by a handler for a message that can never succeed — a payload its event record's <c>TryParse</c> rejects.
/// The dispatcher poisons the message on this attempt instead of retrying it <see cref="OutboxPolicy.MaxAttempts"/>
/// times: a malformed payload is malformed on every attempt.
/// </summary>
/// <remarks>
/// The message is stored as <see cref="OutboxMessage.LastError"/>, so it follows the same rule as any handler
/// failure: name what was wrong in general terms, never quote the payload or a provider's text.
/// </remarks>
public sealed class OutboxPoisonException : Exception
{
    public OutboxPoisonException()
    {
    }

    public OutboxPoisonException(string message)
        : base(message)
    {
    }

    public OutboxPoisonException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
