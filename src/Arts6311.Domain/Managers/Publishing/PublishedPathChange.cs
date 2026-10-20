namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// A published record's public address moving with its slug (content.md, ADR 0035): what a content module's Business
/// hands its DataLayer, which stages <c>redirects.slug-changed</c> in the same save as the rename.
/// </summary>
/// <param name="FromPath">The site path it was published at.</param>
/// <param name="ToPath">The site path it moves to.</param>
public sealed record PublishedPathChange(string FromPath, string ToPath)
{
    /// <summary>
    /// The change for a slug edit, or <c>null</c> when none is owed: the record has never been published (no one can
    /// have the old address), or the slug did not change.
    /// </summary>
    /// <param name="pathPrefix">Where the type lives on the site: <c>/artists/</c>, <c>/exhibitions/</c>, or <c>/</c> for pages.</param>
    public static PublishedPathChange? For(string pathPrefix, string oldSlug, string newSlug, DateTimeOffset? publishedAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(pathPrefix);
        return publishedAt is null || string.Equals(oldSlug, newSlug, StringComparison.Ordinal)
            ? null
            : new PublishedPathChange(pathPrefix + oldSlug, pathPrefix + newSlug);
    }
}
