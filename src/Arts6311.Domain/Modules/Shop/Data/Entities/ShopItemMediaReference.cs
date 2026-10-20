namespace Arts6311.Domain.Modules.Shop.Data.Entities;

/// <summary>
/// A library image a shop item's description shows. An <c>&lt;img&gt;</c> inside HTML is invisible to a foreign key,
/// so each one the sanitizer lists is recorded here, by a <c>Restrict</c> key to <c>MediaAsset</c>: that is how
/// Media's delete guard knows the item uses it (media.md, ADR 0030, 0034).
/// </summary>
/// <remarks>
/// Separate from <see cref="ShopItemImage"/>, which is the gallery staff arrange. A picture can be in both.
/// </remarks>
public sealed class ShopItemMediaReference
{
    public Guid ShopItemId { get; set; }

    public Guid MediaAssetId { get; set; }
}
