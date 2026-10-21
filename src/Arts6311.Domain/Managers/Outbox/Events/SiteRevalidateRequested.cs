using System.Text.Json;
using Arts6311.Domain.Managers.Caching;

namespace Arts6311.Domain.Managers.Outbox.Events;

/// <summary>
/// Says that something the site may be showing has changed (<c>site.revalidate-requested</c>, ADR 0036), so the
/// cache entries derived from it stop being served.
/// </summary>
/// <remarks>
/// <para>
/// In <c>Managers/Outbox/Events/</c> because every content module writes it and one kernel handler applies it
/// (outbox.md), the same arrangement as <c>email.send-requested</c>.
/// </para>
/// <para>
/// <strong>A documented exception to "payloads are ids only"</strong> (ADR 0036): it carries public site paths,
/// on ADR 0035's reasoning. A path is a public URL — not content, a price, a token or anyone's data — and the
/// address a record was published at when the transition committed is a fact no later reload can recover, since
/// the next edit may move it.
/// </para>
/// </remarks>
/// <param name="EntityType">The content type, as its cache tag spells it: <c>artist</c>, <c>exhibition</c>, <c>page</c>, <c>class-offering</c>, <c>shop-item</c>,
/// <c>membership-plan</c>, <c>redirect</c>.</param>
/// <param name="EntityId">Which record changed.</param>
/// <param name="Paths">The site addresses the change affects, each rooted (<c>/artists/jane-doe</c>). May be empty: the type and entity tags still apply.</param>
public sealed record SiteRevalidateRequested(string EntityType, Guid EntityId, IReadOnlyList<string> Paths)
{
    public const string MessageType = "site.revalidate-requested";

    /// <summary>
    /// The most paths one message carries. A transition names its record's address, and a redirect change names
    /// both ends of the hop, so a payload beyond this is a defect rather than a big change.
    /// </summary>
    public const int MaxPaths = 20;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Web);

    /// <summary>The event, or <c>null</c> for anything malformed — which the handler poisons.</summary>
    /// <remarks>
    /// The tag builders refuse an empty segment, a <c>':'</c> inside one and an unrooted path by throwing
    /// (<see cref="CacheTags"/>), so every value they will be handed is checked here instead: a bad payload is a
    /// poisoned message on its first attempt, never an exception five times over.
    /// </remarks>
    public static SiteRevalidateRequested? TryParse(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SiteRevalidateRequested>(payloadJson, Web);
            if (parsed is null
                || !IsTagSegment(parsed.EntityType)
                || parsed.EntityId == Guid.Empty
                || parsed.Paths is null
                || parsed.Paths.Count > MaxPaths
                || parsed.Paths.Any(path => !IsSitePath(path)))
            {
                return null;
            }

            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsTagSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains(':', StringComparison.Ordinal);

    private static bool IsSitePath(string path) =>
        path is ['/', ..] && !path.Contains(':', StringComparison.Ordinal);
}
