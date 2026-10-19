namespace Arts6311.Domain.Managers.Caching;

/// <summary>
/// Builds cache keys and tags. Only strings: this cannot stop an entity being cached, so callers cache
/// ServiceModels only.
/// </summary>
public static class CacheKeys
{
    /// <summary>One item, e.g. <c>artist:jane-doe</c>.</summary>
    public static string Item(string type, string id) =>
        $"{Segment(type, nameof(type))}:{Segment(id, nameof(id))}";

    /// <summary>A list or view of a type, e.g. <c>artist:list:published</c>.</summary>
    public static string List(string type, string variant) =>
        $"{Segment(type, nameof(type))}:list:{Segment(variant, nameof(variant))}";

    // ':' separates segments, so a segment containing one could forge another key's shape:
    // Item("artist", "list:published") would otherwise collide with List("artist", "published").
    internal static string Segment(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value) || value.Contains(':', StringComparison.Ordinal)
            ? throw new ArgumentException("A cache key or tag segment cannot be empty or contain ':'.", paramName)
            : value;
}

/// <summary>
/// Tags that group cache entries for invalidation. A page that shows several artists carries
/// <see cref="Type"/> for the type plus <see cref="Entity"/> for each one, so either kind of change clears it.
/// </summary>
public static class CacheTags
{
    /// <summary>Every entry derived from a content type, e.g. <c>type:artist</c>.</summary>
    public static string Type(string type) => $"type:{CacheKeys.Segment(type, nameof(type))}";

    /// <summary>Every entry derived from one item, e.g. <c>entity:artist:jane-doe</c>.</summary>
    public static string Entity(string type, string id) =>
        $"entity:{CacheKeys.Segment(type, nameof(type))}:{CacheKeys.Segment(id, nameof(id))}";

    /// <summary>Every entry rendered for one public site path, e.g. <c>site:/artists</c>.</summary>
    public static string SitePath(string path)
    {
        CacheKeys.Segment(path, nameof(path));
        return path.StartsWith('/')
            ? $"site:{path}"
            : throw new ArgumentException("A site path starts with '/'.", nameof(path));
    }
}
