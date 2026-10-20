namespace Arts6311.Domain.Modules.Pages.Data.Entities;

/// <summary>
/// A library image a page's body shows. An <c>&lt;img&gt;</c> inside HTML is invisible to a foreign key, so each one the
/// sanitizer lists is recorded here, by a <c>Restrict</c> key to <c>MediaAsset</c>: that is how Media's delete guard
/// knows the page uses it (media.md, ADR 0030, 0034).
/// </summary>
public sealed class PageMediaReference
{
    public Guid PageId { get; set; }

    public Guid MediaAssetId { get; set; }
}
