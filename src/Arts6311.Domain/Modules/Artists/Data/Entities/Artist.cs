using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Artists.Data.Entities;

/// <summary>
/// One artist on the roster (content.md "Content types"): who they are, what they make, and their portrait. Not
/// Square-synced: an artist has no price, stock or catalog object of its own.
/// </summary>
public sealed class Artist : ISoftDeletable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Unique among artists. Locked once <see cref="PublishedAt"/> is set (content.md, ADR 0031).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>What they make, as the site shows it under their name ("Ceramics", "Illustration &amp; prints").</summary>
    public string? Medium { get; set; }

    /// <summary>Plain text; blank lines separate paragraphs. Not HTML (ADR 0031).</summary>
    public string? Bio { get; set; }

    /// <summary>Whether they currently show at the gallery. Independent of <see cref="Status"/>.</summary>
    public bool Active { get; set; }

    /// <summary>
    /// The portrait: an original in the media library, by a <c>Restrict</c> foreign key, so Media refuses to delete
    /// it while it is used here (media.md).
    /// </summary>
    public Guid? PortraitMediaId { get; set; }

    /// <summary>The WordPress product-category term id this artist was imported from (prompt 4.x). Set by the importer only.</summary>
    public long? LegacyWordPressId { get; set; }

    public PublishStatus Status { get; set; }

    /// <summary>
    /// When the artist was first published. Kept through an unpublish, because a URL that has been public stays
    /// someone's bookmark: it is what locks the slug.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>When the scheduled publisher should publish it (2.8). UTC.</summary>
    public DateTimeOffset? ScheduledFor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When staff deleted the artist, or <c>null</c> while it is live (ADR 0052). A deleted record is invisible
    /// to every query — the site included — and only an Admin's restore brings it back, as a Draft.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The user id behind the delete. <c>null</c> while the record is live.</summary>
    public string? DeletedBy { get; set; }

    /// <summary>The user id behind the last write. The importer compares it to tell staff edits from its own (content.md).</summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// The optimistic-concurrency token, rotated by every staff write and carried as the <c>ETag</c>. A
    /// <see cref="Guid"/> the application sets, as on <c>MediaAsset</c>, so the rule holds on the SQLite test database.
    /// </summary>
    public Guid RowVersion { get; set; }
}
