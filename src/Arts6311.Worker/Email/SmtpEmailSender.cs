using Arts6311.Domain.Managers.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Arts6311.Worker.Email;

/// <summary>Where the Worker finds the SMTP server: Aspire's <c>mail</c> connection string (Mailpit locally).</summary>
internal sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>
    /// The <c>mail</c> connection string. Nothing in the repo names a host or a port; the AppHost hands this over
    /// from the MailPit resource.
    /// </summary>
    /// <remarks>
    /// Two shapes are accepted. MailPit actually supplies the keyed form, <c>Endpoint=smtp://host:port</c> —
    /// despite its own documentation describing a bare <c>smtp://host:port</c> — and a bare URI is accepted too so
    /// that a hand-set value or a future version of the integration works either way. Getting this wrong cost five
    /// poisoned delivery attempts before <see cref="Validate"/> existed.
    /// </remarks>
    public string ConnectionString { get; set; } = string.Empty;

    public (string Host, int Port) Endpoint()
    {
        var candidate = ConnectionString;

        // Aspire's keyed form: semicolon-separated key=value pairs, of which Endpoint is the one that matters.
        if (candidate.Contains('=', StringComparison.Ordinal))
        {
            candidate = candidate
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(pair => pair.Split('=', 2))
                .Where(pair => pair.Length == 2 && pair[0].Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair[1])
                .FirstOrDefault() ?? candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new InvalidOperationException(
                "The 'mail' connection string must be an smtp:// URI, optionally in Aspire's 'Endpoint=...' form. "
                + "The AppHost supplies it from the MailPit resource.");
        }

        return (uri.Host, uri.Port > 0 ? uri.Port : 25);
    }

    /// <summary>
    /// Checked at startup, so a missing or unreadable value stops the Worker instead of poisoning every email it
    /// is asked to send. The failure is the same either way; this one is visible immediately and costs no
    /// delivery attempts.
    /// </summary>
    public void Validate() => Endpoint();
}

/// <summary>
/// Sends email over SMTP — Mailpit on a laptop, and a real relay wherever this is deployed until 6.1 adds the
/// Azure Communication Services sender beside it.
/// </summary>
/// <remarks>
/// <para>
/// It lives in the Worker because the Worker is the only process that ever sends: every email in the system is
/// an <c>email.send-requested</c> outbox message. Keeping MailKit here means the API and the gateway carry no
/// SMTP client at all (external.md, ADR 0023).
/// </para>
/// <para>
/// A new connection per message. The dispatcher sends one email every few seconds at most, so a pooled
/// connection would spend its life idle and time out, and a stale pooled connection is a harder failure to
/// diagnose than a fresh connect.
/// </para>
/// </remarks>
internal sealed class SmtpEmailSender(IOptions<SmtpOptions> smtp, IOptions<EmailOptions> email) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var options = email.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.Subject = message.Subject;

        try
        {
            // Parse, not construct: it rejects CRLF and other header-injection attempts in an address. An
            // unparseable address will never become parseable, so it is permanent rather than retried.
            mime.To.Add(MailboxAddress.Parse(message.To));
        }
        catch (ParseException exception)
        {
            throw new EmailPermanentlyUndeliverableException("The recipient address could not be parsed.", exception);
        }

        var body = new BodyBuilder { TextBody = message.TextBody };
        if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            body.HtmlBody = message.HtmlBody;
        }

        mime.Body = body.ToMessageBody();

        var (host, port) = smtp.Value.Endpoint();

        using var client = new SmtpClient
        {
            // Well under the outbox lease (5 minutes). MailKit's default is 120 seconds *per operation*, so a
            // connect, a send and a disconnect could together outlive the lease — at which point the dispatcher
            // reclaims the message, a second run starts, and the same email goes out twice.
            Timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds,
        };

        // Mailpit listens plaintext on a loopback port and offers no STARTTLS. Auto upgrades when the server
        // advertises it, so a real relay still gets TLS without this becoming configuration.
        await client.ConnectAsync(host, port, SecureSocketOptions.Auto, cancellationToken);

        try
        {
            await client.SendAsync(mime, cancellationToken);
        }
        catch (SmtpCommandException exception) when ((int)exception.StatusCode >= 500)
        {
            // A 5xx is the server saying no permanently — an unknown mailbox, a rejected sender. Retrying it four
            // more times only delays the moment anybody notices.
            throw new EmailPermanentlyUndeliverableException(
                "The server refused the message permanently.", exception);
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
