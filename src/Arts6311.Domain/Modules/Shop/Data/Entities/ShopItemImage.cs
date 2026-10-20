namespace Arts6311.Domain.Modules.Shop.Data.Entities;

/// <summary>
/// One photograph of a shop item: a library original, by a <c>Restrict</c> foreign key, so Media refuses to delete
/// it while the shop uses it (media.md).
/// </summary>
public sealed class ShopItemImage
{
    public Guid ShopItemId { get; set; }

    public Guid MediaAssetId { get; set; }

    /// <summary>Where it comes in the gallery, from 0. The first is the lead photograph (ADR 0038).</summary>
    public int Position { get; set; }
}
