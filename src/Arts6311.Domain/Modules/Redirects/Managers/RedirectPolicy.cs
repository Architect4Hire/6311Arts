namespace Arts6311.Domain.Modules.Redirects.Managers;

/// <summary>Limits and settings for redirects. The admin's form uses the same numbers (<c>REDIRECT_LIMITS</c>).</summary>
public static class RedirectPolicy
{
    /// <summary>The longest old address (path and query) kept: 400 characters, an indexable <c>nvarchar</c>.</summary>
    public const int FromMaxLength = 400;

    /// <summary>The longest destination kept: a site path or a full <c>https://</c> address.</summary>
    public const int ToMaxLength = 800;

    /// <summary>The status codes a redirect may answer with (content.md): permanent, or temporary.</summary>
    public static IReadOnlySet<int> StatusCodes { get; } = new HashSet<int> { 301, 302 };

    public const int DefaultStatusCode = 301;

    /// <summary>The longest search text the list accepts.</summary>
    public const int SearchMaxLength = 100;

    /// <summary>
    /// How long the site's copy of the list is cached (HybridCache and <c>Cache-Control</c>). Until 2.8's
    /// <c>site.revalidate-requested</c> clears it on a change, a change shows within this long (ADR 0035).
    /// </summary>
    public static readonly TimeSpan SiteCacheLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Identity's user id column length, so <c>UpdatedBy</c> can hold any user id.</summary>
    internal const int UserIdMaxLength = 450;
}
