using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Shop.Data.Entities;

/// <summary>
/// One piece of artwork the shop sells (content.md "Content types"): a print, a necklace, a bowl, a one-of-a-kind
/// piece. <strong>Square-synced</strong> (ADR 0032): the CMS owns this content, its SKU and its starting figures;
/// Square owns the record of money and stock.
/// </summary>
/// <remarks>
/// Artwork only. Merch — 6311arts-branded goods — has no type here, no site page and no import mapping: it is sold
/// at the front desk through Square POS (ADR 0032, 0038).
/// </remarks>
public sealed class ShopItem
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Unique among shop items. A change after publish redirects the old address (content.md, ADR 0035).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Sanitized HTML (<c>IHtmlSanitizer</c>); never stored any other way (ADR 0034, 0038). The site's meta
    /// description comes from it, so publishing requires it.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Who made it, when the shop knows: the Artists module's record, by a <c>Restrict</c> foreign key only. Who
    /// they are is asked of <c>IArtistsFacade</c> (ADR 0033). Optional — the spike's magazines credit nobody.
    /// </summary>
    public Guid? ArtistId { get; set; }

    /// <summary>What the site filters on, from <c>ShopPolicy.Categories</c> (ADR 0038).</summary>
    public List<ShopItemCategory> Categories { get; set; } = [];

    /// <summary>The photographs, in the order someone chose. The first is the lead (ADR 0038).</summary>
    public List<ShopItemImage> Images { get; set; } = [];

    /// <summary>The library images the description shows, kept in step with it on every write.</summary>
    public List<ShopItemMediaReference> DescriptionImages { get; set; } = [];

    /// <summary>
    /// What it is sold under. <strong>CMS-owned</strong> and unique across the whole CMS, which the shared
    /// <c>CatalogSkus</c> registry enforces (ADR 0037). Suggested on create, editable by staff.
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// The item's number, assigned on create, which its suggested SKU is built from (<c>ART-&lt;item&gt;</c>,
    /// ADR 0037). A suggestion aid, not an identity.
    /// </summary>
    public int ItemNumber { get; set; }

    /// <summary>
    /// One of a kind: there is exactly one, so <see cref="StartingStockQuantity"/> is 1 and Square never gets a
    /// set-quantity command for it (ADR 0032). Frozen once Square holds the record (ADR 0038).
    /// </summary>
    public bool OneOfAKind { get; set; }

    /// <summary>
    /// What it costs when the first push creates the Square objects (ADR 0032). A draft field until then, and
    /// frozen once Square has reported money or stock: from that point price changes are commands (5.4a).
    /// </summary>
    public long? StartingPriceCents { get; set; }

    /// <summary>
    /// How many there are when the first push creates the Square objects. 1 for a one-of-a-kind piece. Frozen once
    /// Square holds the record, after which quantity moves through the set-quantity command (5.4b).
    /// </summary>
    public int? StartingStockQuantity { get; set; }

    /// <summary>
    /// Square's record of the price, in cents. A <strong>read-only projection</strong>, written only by the inbound
    /// pull through the <c>SquareSync</c> facade (square.md, ADR 0032). <c>null</c> until Square has reported one.
    /// </summary>
    public long? PriceCents { get; set; }

    /// <summary>
    /// Square's record of how many are in stock. A <strong>read-only projection</strong>; never recomputed from
    /// orders (square.md). <c>null</c> until the first pull.
    /// </summary>
    public int? StockOnHand { get; set; }

    /// <summary>Square's record of whether it is sold out. A <strong>read-only projection</strong>.</summary>
    public bool SoldOut { get; set; }

    /// <summary>The WooCommerce product id this item was imported from (prompt 4.x). Set by the importer only.</summary>
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
    /// The optimistic-concurrency token, rotated by every staff write (a change to the gallery or the categories
    /// too) and carried as the <c>ETag</c>.
    /// </summary>
    public Guid RowVersion { get; set; }

    /// <summary>Whether Square has told us anything about this item's money or stock yet (ADR 0037, 0038).</summary>
    public bool HasSquareRecord => PriceCents is not null || StockOnHand is not null;
}
