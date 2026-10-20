using System.Text.Json;

namespace Arts6311.Domain.Modules.Media.Managers;

/// <summary>
/// Asks the Worker to produce an original's renditions (outbox.md's <c>media.rendition-requested</c>). Staged in the
/// same save as the upload, and again when the compression preset changes.
/// </summary>
/// <remarks>
/// In this module rather than <c>Managers/Outbox/Events/</c>: Media both writes and handles it. The payload is the
/// asset id only; the handler reloads the asset and its current preset, so a stale or replayed message renders
/// what the asset is now.
/// </remarks>
public sealed record MediaRenditionRequested(Guid AssetId)
{
    public const string MessageType = "media.rendition-requested";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Web);

    /// <summary>The event, or <c>null</c> for anything malformed or with an empty id — which the handler poisons.</summary>
    public static MediaRenditionRequested? TryParse(string payloadJson) => Parse<MediaRenditionRequested>(payloadJson, evt => evt.AssetId);

    internal static T? Parse<T>(string payloadJson, Func<T, Guid> id)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(payloadJson, Web);
            return parsed is null || id(parsed) == Guid.Empty ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Asks the Worker to delete a deleted asset's blobs. Removing bytes from storage is a side effect of the delete's
/// commit, so it is an outbox message like any other (outbox.md); the rows are already gone when it runs.
/// </summary>
/// <remarks>
/// The id alone is enough: every blob an asset owns is under <c>{assetId}/</c> (<see cref="MediaBlobKeys"/>), and
/// asset ids are never reused, so purging the prefix again is harmless.
/// </remarks>
public sealed record MediaPurgeRequested(Guid AssetId)
{
    public const string MessageType = "media.purge-requested";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Web);

    public static MediaPurgeRequested? TryParse(string payloadJson) =>
        MediaRenditionRequested.Parse<MediaPurgeRequested>(payloadJson, evt => evt.AssetId);
}
