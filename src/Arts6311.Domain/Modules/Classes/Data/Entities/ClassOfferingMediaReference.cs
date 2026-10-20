namespace Arts6311.Domain.Modules.Classes.Data.Entities;

/// <summary>
/// A library image an offering's body shows. An <c>&lt;img&gt;</c> inside HTML is invisible to a foreign key, so each
/// one the sanitizer lists is recorded here, by a <c>Restrict</c> key to <c>MediaAsset</c>: that is how Media's
/// delete guard knows the offering uses it (media.md, ADR 0030, 0034).
/// </summary>
public sealed class ClassOfferingMediaReference
{
    public Guid ClassOfferingId { get; set; }

    public Guid MediaAssetId { get; set; }
}
