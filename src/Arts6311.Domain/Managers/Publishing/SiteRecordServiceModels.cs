namespace Arts6311.Domain.Managers.Publishing;

/// <summary>One address for <c>/sitemap.xml</c>: a published record's site path and when it last changed.</summary>
/// <param name="Path">Rooted, as the site serves it: <c>/artists/jane-doe</c>.</param>
/// <param name="LastModified">The record's last change (UTC), for <c>&lt;lastmod&gt;</c>.</param>
public sealed record SitemapEntryServiceModel(string Path, DateTimeOffset LastModified);

/// <summary>
/// A record as the site would show it, whatever its status, for a preview link (ADR 0036, 0042). The status rides
/// beside the site's own model rather than inside it, because the site's models never carry one.
/// </summary>
public sealed record PreviewRecord<T>(T Record, PublishStatus Status)
    where T : class;
