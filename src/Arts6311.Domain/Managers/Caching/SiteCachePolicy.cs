namespace Arts6311.Domain.Managers.Caching;

/// <summary>
/// How long the site's reads are kept, in the API's cache and by anything in front of it (ADR 0042).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every site entry carries the type tag of every content type it shows</strong>, never an entity or path
/// tag alone: HybridCache takes the tags before the factory runs, and a read by slug does not know the record's id
/// yet; a rename's revalidation names only the new address, so an entry for the old slug would outlive it. "Any
/// change to a type clears every site entry built from it" is right for renames, for embedded credits (an artist's
/// new name on a shop item) and for unpublishing alike, and staff write rarely enough that clearing a whole type
/// costs nothing (ADR 0042).
/// </para>
/// <para>
/// The revalidation handler reaches the shared L2 at once and every other process through the cache invalidation bus
/// (ADR 0049); <see cref="Lifetime"/> bounds a change nothing announced (a rendition finishing, a session's start
/// passing) and a bus message a process missed.
/// </para>
/// </remarks>
public static class SiteCachePolicy
{
    /// <summary>How long a site read stays in HybridCache when nothing invalidates it first.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// <c>Cache-Control: public, max-age</c> on a site read: how long anything in front of the API may keep it. The
    /// worst case is <see cref="Lifetime"/>, when a read racing a commit stores what it read after the revalidation
    /// cleared its tags (ADR 0042), or a process missed the bus message (ADR 0049).
    /// </summary>
    public static readonly TimeSpan HttpMaxAge = TimeSpan.FromMinutes(1);
}
