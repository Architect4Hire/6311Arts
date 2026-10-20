using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Classes.Data.Entities;

/// <summary>
/// One class or workshop the centre teaches (content.md "Content types"): what it is, who teaches it, how often it
/// meets, and the sittings staff can book. <strong>Square-synced</strong> (ADR 0032): the CMS owns this content and
/// each session's SKU and capacity; Square owns the record of money and seats.
/// </summary>
public sealed class ClassOffering
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Unique among offerings. A change after publish redirects the old address (content.md, ADR 0035).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>One or two sentences: the card, the meta description and the search result. Required to publish.</summary>
    public string? Summary { get; set; }

    /// <summary>Sanitized HTML (<c>IHtmlSanitizer</c>); never stored any other way (ADR 0034, 0037).</summary>
    public string? Body { get; set; }

    /// <summary>The medium or kind: "Printmaking", "Mixed media", "Talk". Free text, as the spike's classes are.</summary>
    public string? ClassType { get; set; }

    /// <summary>Who it is for: "Beginner", "All levels".</summary>
    public string? Level { get; set; }

    /// <summary>Who teaches it, as the site prints it. A name, not a link to the artist roster.</summary>
    public string? Instructor { get; set; }

    /// <summary>How a multi-session course meets: "Saturdays", "Wednesdays, 5 weeks".</summary>
    public string? Cadence { get; set; }

    /// <summary>
    /// What one seat costs when the first push creates the Square objects (ADR 0032). A draft field until then, and
    /// frozen once Square has reported money or seats for any session: from that point price changes are commands
    /// (5.4a).
    /// Never written by an inbound pull — <see cref="ClassSession.PriceCents"/> is what Square's record lands in.
    /// </summary>
    public long? StartingPriceCents { get; set; }

    /// <summary>
    /// The offering's number, assigned on create, which its sessions' suggested SKUs are built from
    /// (<c>CLS-&lt;offering&gt;-&lt;session&gt;</c>, ADR 0037). A suggestion aid, not an identity.
    /// </summary>
    public int OfferingNumber { get; set; }

    /// <summary>
    /// The hero image: an original in the media library, by a <c>Restrict</c> foreign key, so Media refuses to delete
    /// it while it is used here (media.md).
    /// </summary>
    public Guid? HeroMediaId { get; set; }

    /// <summary>The library images the body shows, kept in step with it on every write so Media's delete guard sees them.</summary>
    public List<ClassOfferingMediaReference> Images { get; set; } = [];

    /// <summary>The sittings, in start order. Part of this offering's aggregate: one <c>ETag</c> covers both.</summary>
    public List<ClassSession> Sessions { get; set; } = [];

    /// <summary>The WordPress event id this offering was imported from (prompt 4.x). Set by the importer only.</summary>
    public long? LegacyWordPressId { get; set; }

    public PublishStatus Status { get; set; }

    /// <summary>When it was first published. Kept through an unpublish: it is what makes a slug change owe a redirect.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>When the scheduled publisher should publish it (ADR 0036). UTC.</summary>
    public DateTimeOffset? ScheduledFor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The user id behind the last write. The importer compares it to tell staff edits from its own (content.md).</summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// The optimistic-concurrency token, rotated by every staff write (a change to the sessions too) and carried as
    /// the <c>ETag</c>. A <see cref="Guid"/> the application sets, as on <c>Exhibition</c>.
    /// </summary>
    public Guid RowVersion { get; set; }
}
