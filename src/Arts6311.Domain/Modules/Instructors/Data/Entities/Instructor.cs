using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Instructors.Data.Entities;

/// <summary>
/// One person who teaches at the centre (content.md "Content types", ADR 0043): who they are, what they teach, their
/// portrait and links, and, when they also sell work in the shop, the artist record that work is credited to. A class
/// run names its instructor by id. Not Square-synced: an instructor has no price, stock or catalog object of its own.
/// </summary>
public sealed class Instructor : ISoftDeletable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Unique among instructors. A change after publish redirects the old address (content.md, ADR 0035).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>What they teach here, as the site shows it under their name ("Printmaking", "Talks &amp; art history").</summary>
    public string? Medium { get; set; }

    /// <summary>Plain text; blank lines separate paragraphs. Not HTML, as an artist's bio (ADR 0031).</summary>
    public string? Bio { get; set; }

    /// <summary>Whether they currently teach at the centre. Independent of <see cref="Status"/>.</summary>
    public bool Active { get; set; }

    /// <summary>
    /// The portrait: an original in the media library, by a <c>Restrict</c> foreign key, so Media refuses to delete
    /// it while it is used here (media.md).
    /// </summary>
    public Guid? PortraitMediaId { get; set; }

    /// <summary>Their own site: an absolute <c>https:</c> address (<c>InstructorPolicy.IsWebsite</c>).</summary>
    public string? Website { get; set; }

    /// <summary>Their Instagram handle, without the <c>@</c> (<c>InstructorPolicy.IsInstagramHandle</c>).</summary>
    public string? Instagram { get; set; }

    /// <summary>
    /// The artist on the roster this instructor also is, so their page can link to their work in the shop. A
    /// <c>Restrict</c> key into Artists, as a shop item's credit (ADR 0033): an artist linked here cannot vanish, and
    /// archive is how one is retired.
    /// </summary>
    public Guid? ArtistId { get; set; }

    /// <summary>The WordPress record this instructor was imported from (prompt 4.x). Set by the importer only.</summary>
    public long? LegacyWordPressId { get; set; }

    public PublishStatus Status { get; set; }

    /// <summary>When the instructor was first published. Kept through an unpublish: it is what makes a slug change owe a redirect.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>When the scheduled publisher should publish it (ADR 0036). UTC.</summary>
    public DateTimeOffset? ScheduledFor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When staff deleted the instructor, or <c>null</c> while it is live (ADR 0052). A deleted record is invisible
    /// to every query — the site included — and only an Admin's restore brings it back, as a Draft.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The user id behind the delete. <c>null</c> while the record is live.</summary>
    public string? DeletedBy { get; set; }

    /// <summary>The user id behind the last write. The importer compares it to tell staff edits from its own (content.md).</summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// The optimistic-concurrency token, rotated by every staff write and carried as the <c>ETag</c>. A
    /// <see cref="Guid"/> the application sets, as on <c>Artist</c>, so the rule holds on the SQLite test database.
    /// </summary>
    public Guid RowVersion { get; set; }
}
