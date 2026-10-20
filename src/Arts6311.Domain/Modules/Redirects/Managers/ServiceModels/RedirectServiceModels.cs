using System.Text.Json.Serialization;

namespace Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;

/// <summary>One redirect, as the CMS lists and edits it.</summary>
/// <param name="FromPath">The old address, in normal form: a lower-case site path, with any query.</param>
/// <param name="ToPath">Where it leads: a site path (with any query and fragment) or a full <c>https://</c> address.</param>
/// <param name="StatusCode">301 (moved for good) or 302 (for now).</param>
/// <param name="Source">Who made it: staff, the WordPress import, or a slug change.</param>
/// <param name="LegacyWordPressId">The WordPress redirect it was imported from, for provenance.</param>
/// <param name="ETag">The row version, to send back as <c>If-Match</c>.</param>
public sealed record RedirectServiceModel(
    Guid Id,
    string FromPath,
    string ToPath,
    int StatusCode,
    RedirectSource Source,
    long? LegacyWordPressId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    [property: JsonPropertyName("etag")] string ETag);

/// <summary>One redirect as the public site applies it: nothing about who made it.</summary>
public sealed record SiteRedirectServiceModel(string FromPath, string ToPath, int StatusCode);
