using System.Text.Json;

namespace Arts6311.Domain.Managers.Outbox.Events;

/// <summary>
/// Asks the Worker to send one transactional email (outbox.md's <c>email.send-requested</c>).
/// </summary>
/// <remarks>
/// It lives in <c>Managers/Outbox/Events/</c> rather than in a module because many modules write it — Identity
/// today, Forms, Commerce and Memberships later — while one kernel handler delivers it (outbox.md).
/// </remarks>
/// <param name="Kind">
/// Which email, e.g. <c>identity.password-reset</c>. Routes to the <c>IEmailComposer</c> registered under it by
/// the module that owns the kind.
/// </param>
/// <param name="SubjectId">
/// What the email is about: a user id, a submission id, an order id. <strong>Ids only</strong> — no address, no
/// token, no body. The composer resolves the recipient and mints any token at send time (add-outbox-message).
/// </param>
public sealed record EmailSendRequested(string Kind, string SubjectId)
{
    public const string MessageType = "email.send-requested";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Web);

    /// <summary>The event, or <c>null</c> for anything malformed or missing a field — which the handler poisons.</summary>
    public static EmailSendRequested? TryParse(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<EmailSendRequested>(payloadJson, Web);
            return parsed is null
                || string.IsNullOrWhiteSpace(parsed.Kind)
                || string.IsNullOrWhiteSpace(parsed.SubjectId)
                    ? null
                    : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
