namespace Arts6311.Domain.Managers.Email;

/// <summary>
/// A record that one outbox message's email has been dealt with, so a redelivery does not send it again.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the <strong>outbox message id</strong>, not on (kind, subject, recipient) as add-notification
/// suggests. Someone may legitimately ask for a second password reset, and keying on the subject would swallow
/// it silently. The message id gives exactly what is wanted: one delivery per message, however many times the
/// dispatcher hands it over (ADR 0023).
/// </para>
/// <para>
/// It holds no address. <see cref="RecipientHash"/> is a SHA-256 of the lower-cased address, which is enough to
/// ask "did this person get it?" in support without keeping a second copy of everyone's email beside the user
/// table.
/// </para>
/// </remarks>
public sealed class EmailDelivery
{
    public Guid Id { get; set; }

    /// <summary>The outbox message this delivery belongs to. Unique.</summary>
    public Guid OutboxMessageId { get; set; }

    /// <summary>Which email, for support and for counting what was sent.</summary>
    public string Kind { get; set; } = string.Empty;

    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Hex SHA-256 of the lower-cased recipient address, or <c>null</c> when nothing was sent.</summary>
    public string? RecipientHash { get; set; }

    public DateTimeOffset HandledAt { get; set; }

    /// <summary>
    /// False when the composer had nothing to send. Recorded rather than left absent so a missing email can be
    /// explained without reading the logs.
    /// </summary>
    public bool Sent { get; set; }
}

/// <summary>Column limits for <see cref="EmailDelivery"/>.</summary>
public static class EmailDeliveryPolicy
{
    public const int KindMaxLength = 100;
    public const int SubjectIdMaxLength = 450;
    public const int RecipientHashLength = 64;
}
