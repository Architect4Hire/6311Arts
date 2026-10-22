using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Pages.Data.Entities;

/// <summary>
/// A standalone page (content.md "Content types"): privacy, terms and the site's other copy blocks. Its body is HTML,
/// and only ever the shared sanitizer's output (ADR 0034).
/// </summary>
public sealed class Page : ISoftDeletable
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Unique among pages. Locked once <see cref="PublishedAt"/> is set (content.md, ADR 0031).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Sanitized HTML (<c>IHtmlSanitizer</c>); never stored any other way. Required to publish.</summary>
    public string? Body { get; set; }

    /// <summary>The library images the body shows, kept in step with it on every write so Media's delete guard sees them.</summary>
    public List<PageMediaReference> Images { get; set; } = [];

    /// <summary>The WordPress page id this page was imported from (prompt 4.x). Set by the importer only.</summary>
    public long? LegacyWordPressId { get; set; }

    public PublishStatus Status { get; set; }

    /// <summary>When the page was first published. Kept through an unpublish: it is what locks the slug.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>When the scheduled publisher should publish it (2.8). UTC.</summary>
    public DateTimeOffset? ScheduledFor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When staff deleted the page, or <c>null</c> while it is live (ADR 0052). A deleted page is invisible to
    /// every query — the site included — and only an Admin's restore brings it back, as a Draft.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The user id behind the delete. <c>null</c> while the page is live.</summary>
    public string? DeletedBy { get; set; }

    /// <summary>The user id behind the last write. The importer compares it to tell staff edits from its own (content.md).</summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>The optimistic-concurrency token, rotated by every staff write and carried as the <c>ETag</c>.</summary>
    public Guid RowVersion { get; set; }
}
