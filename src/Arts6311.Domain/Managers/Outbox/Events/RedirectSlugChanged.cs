using System.Text.Json;

namespace Arts6311.Domain.Managers.Outbox.Events;

/// <summary>
/// Tells the Redirects module that a published record's public address moved (<c>redirects.slug-changed</c>,
/// ADR 0035), so the old address can lead to the new one.
/// </summary>
/// <remarks>
/// <para>
/// In <c>Managers/Outbox/Events/</c> because several content modules write it (Artists, Exhibitions, Pages, and the
/// Square-synced types later) and the Redirects module handles it (outbox.md).
/// </para>
/// <para>
/// <strong>A documented exception to "payloads are ids only"</strong> (ADR 0035): it carries the two paths. The old
/// address is an immutable fact of the rename that no reload could recover, and a path is a public URL — not content,
/// a price, a token or anyone's data. <paramref name="OccurredAt"/> lets the handler apply renames delivered out of
/// order without undoing a later one.
/// </para>
/// </remarks>
/// <param name="ContentType">Which kind of record moved (<c>artist</c>, <c>exhibition</c>, <c>page</c>), for the audit trail.</param>
/// <param name="ContentId">Which record moved, for the audit trail.</param>
/// <param name="FromPath">The site path it was published at, e.g. <c>/artists/jane-doe</c>.</param>
/// <param name="ToPath">The site path it is at now.</param>
/// <param name="OccurredAt">When the rename was saved (UTC): the record's <c>UpdatedAt</c>.</param>
public sealed record RedirectSlugChanged(
    string ContentType, Guid ContentId, string FromPath, string ToPath, DateTimeOffset OccurredAt)
{
    public const string MessageType = "redirects.slug-changed";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Web);

    /// <summary>The event, or <c>null</c> for anything malformed or missing a field — which the handler poisons.</summary>
    public static RedirectSlugChanged? TryParse(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RedirectSlugChanged>(payloadJson, Web);
            return parsed is null
                || string.IsNullOrWhiteSpace(parsed.ContentType)
                || parsed.ContentId == Guid.Empty
                || parsed.FromPath is not ['/', _, ..]
                || parsed.ToPath is not ['/', _, ..]
                || parsed.OccurredAt == default
                    ? null
                    : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
