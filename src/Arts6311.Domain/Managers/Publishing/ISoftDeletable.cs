namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// A CMS record staff can delete and an Admin can restore (content.md "Deletion", ADR 0052). Deletion is not a
/// <see cref="PublishStatus"/>: <c>Status</c> keeps saying what the record was when it was deleted, which is what
/// the Deleted view shows and what a restore reports.
/// </summary>
/// <remarks>
/// The entity's own configuration hides deleted rows from every query with <c>SoftDelete.Apply</c>, so nothing —
/// the CMS lists, the site reads, the sitemap, previews, the scheduled publisher, slug checks, another module's
/// link check — sees one unless it asks for it by name with <c>IgnoreQueryFilters()</c>.
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>When the record was deleted, or <c>null</c> while it is live. UTC.</summary>
    DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The user id behind the delete, kept beside the audit row. <c>null</c> while the record is live.</summary>
    string? DeletedBy { get; set; }
}
