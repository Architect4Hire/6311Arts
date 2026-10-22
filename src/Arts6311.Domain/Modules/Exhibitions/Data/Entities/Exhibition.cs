using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Exhibitions.Data.Entities;

/// <summary>
/// One show in the gallery (content.md "Content types"): what it is, when it hangs, its opening reception, its hero
/// image and the artists it credits. Not Square-synced: an exhibition has no price or stock.
/// </summary>
public sealed class Exhibition : ISoftDeletable
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Unique among exhibitions. Locked once <see cref="PublishedAt"/> is set (content.md, ADR 0031).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>One or two sentences: the card, the meta description and the search result. Required to publish.</summary>
    public string? Summary { get; set; }

    /// <summary>Plain text; blank lines separate paragraphs. Not HTML until the sanitizer (2.6, ADR 0033).</summary>
    public string? Body { get; set; }

    /// <summary>The first day the show hangs, as a calendar date in the gallery's time zone.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>The last day the show hangs, inclusive. Never before <see cref="StartDate"/>.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>When the opening reception starts. UTC; set together with <see cref="ReceptionEndsAt"/> or not at all.</summary>
    public DateTimeOffset? ReceptionStartsAt { get; set; }

    /// <summary>When the reception ends. UTC; after <see cref="ReceptionStartsAt"/>.</summary>
    public DateTimeOffset? ReceptionEndsAt { get; set; }

    /// <summary>A line under the reception time ("Free and open to all"). Only with a reception.</summary>
    public string? ReceptionNote { get; set; }

    /// <summary>
    /// The hero image: an original in the media library, by a <c>Restrict</c> foreign key, so Media refuses to delete
    /// it while it is used here (media.md).
    /// </summary>
    public Guid? HeroMediaId { get; set; }

    /// <summary>The artists the show credits, in the order the site lists them.</summary>
    public List<ExhibitionArtist> Artists { get; set; } = [];

    /// <summary>The WordPress post id this exhibition was imported from (prompt 4.x). Set by the importer only.</summary>
    public long? LegacyWordPressId { get; set; }

    public PublishStatus Status { get; set; }

    /// <summary>When the exhibition was first published. Kept through an unpublish: it is what locks the slug.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>When the scheduled publisher should publish it (2.8). UTC.</summary>
    public DateTimeOffset? ScheduledFor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When staff deleted the exhibition, or <c>null</c> while it is live (ADR 0052). A deleted record is invisible
    /// to every query — the site included — and only an Admin's restore brings it back, as a Draft.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The user id behind the delete. <c>null</c> while the record is live.</summary>
    public string? DeletedBy { get; set; }

    /// <summary>The user id behind the last write. The importer compares it to tell staff edits from its own (content.md).</summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// The optimistic-concurrency token, rotated by every staff write (a change to the credits too) and carried as
    /// the <c>ETag</c>. A <see cref="Guid"/> the application sets, as on <c>Artist</c>.
    /// </summary>
    public Guid RowVersion { get; set; }
}
