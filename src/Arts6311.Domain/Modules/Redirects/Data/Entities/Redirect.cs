using Arts6311.Domain.Modules.Redirects.Managers;

namespace Arts6311.Domain.Modules.Redirects.Data.Entities;

/// <summary>
/// One old address and where it now leads (content.md "Content types"). Live as soon as it exists: a redirect has
/// no publish lifecycle. Every chain is one hop: no redirect's destination is another redirect's old address.
/// </summary>
public sealed class Redirect
{
    public Guid Id { get; set; }

    /// <summary>The old address in normal form (<see cref="RedirectPaths.NormalizeFrom"/>). Unique.</summary>
    public string FromPath { get; set; } = string.Empty;

    /// <summary>The destination in normal form (<see cref="RedirectPaths.NormalizeTo"/>): a site path or an https address.</summary>
    public string ToPath { get; set; } = string.Empty;

    /// <summary>
    /// The site address <see cref="ToPath"/> arrives at, without its fragment; <c>null</c> for another site. Kept so
    /// loop and chain checks are one indexed lookup (<see cref="RedirectPaths.TargetOf"/>).
    /// </summary>
    public string? TargetPath { get; set; }

    /// <summary>301 or 302.</summary>
    public int StatusCode { get; set; }

    public RedirectSource Source { get; set; }

    /// <summary>
    /// When what this redirect says became true: the rename's time for a <see cref="RedirectSource.SlugChange"/>,
    /// the write's for the rest. A slug change delivered late compares against it, so it never undoes a newer one.
    /// </summary>
    public DateTimeOffset EffectiveAt { get; set; }

    /// <summary>The WordPress redirect it was imported from (prompt 4.x). Set by the importer only.</summary>
    public long? LegacyWordPressId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The user id behind the last write, or a <c>system:</c> actor for the seed and slug changes.</summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>The optimistic-concurrency token, rotated by every write and carried as the <c>ETag</c>.</summary>
    public Guid RowVersion { get; set; }
}
