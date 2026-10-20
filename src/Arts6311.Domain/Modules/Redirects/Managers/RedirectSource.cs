using System.Text.Json.Serialization;

namespace Arts6311.Domain.Modules.Redirects.Managers;

/// <summary>
/// Where a redirect came from (content.md, ADR 0035). Sent as names. Ordered, not a bitmask.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RedirectSource>))]
public enum RedirectSource
{
    /// <summary>Written by staff in the CMS.</summary>
    Manual = 0,

    /// <summary>Carried over from the WordPress site (the importer, and the spike's fixed legacy addresses).</summary>
    Imported = 10,

    /// <summary>Made by the system when a published record's slug changed; kept one hop by the same mechanism.</summary>
    SlugChange = 20,
}
