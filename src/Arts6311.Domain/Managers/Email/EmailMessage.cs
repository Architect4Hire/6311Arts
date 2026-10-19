namespace Arts6311.Domain.Managers.Email;

/// <summary>
/// One composed email, ready to send.
/// </summary>
/// <remarks>
/// Never carries card data, a full postal address or staff-only notes (add-notification). Links are built from
/// the configured public origin and re-authorize when opened; the only tokens that may appear in one are
/// Identity's own confirmation and reset tokens.
/// </remarks>
/// <param name="To">The recipient address, resolved by the composer from the subject — never from the payload.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="TextBody">The plain-text body. Required: every email has one, so no client is left with nothing.</param>
/// <param name="HtmlBody">An optional HTML alternative.</param>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);
