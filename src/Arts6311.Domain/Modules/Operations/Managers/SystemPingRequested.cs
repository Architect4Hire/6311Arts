using System.Text.Json;

namespace Arts6311.Domain.Modules.Operations.Managers;

/// <summary>
/// Development scaffolding (prompt 1.5): asks the Worker to record that a ping travelled API → outbox → handler.
/// Written and handled by this module only, so it lives here rather than in <c>Managers/Outbox/Events/</c>.
/// Removed with the ping route in 1.12.
/// </summary>
/// <param name="PingId">The server-generated ping id. Ids only (outbox.md).</param>
internal sealed record SystemPingRequested(Guid PingId)
{
    public const string MessageType = "system.ping-requested";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Web);

    /// <summary>The event, or <c>null</c> for anything malformed or with an empty id — which the handler poisons.</summary>
    public static SystemPingRequested? TryParse(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SystemPingRequested>(payloadJson, Web);
            return parsed is null || parsed.PingId == Guid.Empty ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
