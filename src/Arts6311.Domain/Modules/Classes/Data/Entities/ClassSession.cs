namespace Arts6311.Domain.Modules.Classes.Data.Entities;

/// <summary>
/// One sitting of a class: when it meets, how many seats it has, and the SKU it is sold under. Each session is its
/// own Square variation, so each carries its own price and seat count (ADR 0032, 0037).
/// </summary>
public sealed class ClassSession
{
    public Guid Id { get; set; }

    public Guid ClassOfferingId { get; set; }

    /// <summary>When it starts. UTC, as everything stored is (backend.md).</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>When it ends. After <see cref="StartsAt"/>.</summary>
    public DateTimeOffset EndsAt { get; set; }

    /// <summary>
    /// The IANA zone its times are shown in (backend.md: "class sessions also store the display time zone id").
    /// The gallery's own zone unless staff change it, so an online session can be advertised in another.
    /// </summary>
    public string DisplayTimeZone { get; set; } = string.Empty;

    /// <summary>
    /// What it is sold under. <strong>CMS-owned</strong> and unique across the whole CMS, which the shared
    /// <c>CatalogSkus</c> registry enforces (ADR 0037). Suggested on create, editable by staff.
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// How many seats it has. <strong>CMS-owned</strong> (ADR 0032): the starting value creates the Square seat
    /// count, and a later change stages a seat adjustment by the difference (5.4a).
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Square's record of the price, in cents. A <strong>read-only projection</strong>, written only by the inbound
    /// pull through the <c>SquareSync</c> facade (square.md, ADR 0032). <c>null</c> until Square has reported one.
    /// </summary>
    public long? PriceCents { get; set; }

    /// <summary>
    /// Square's record of the seats left. A <strong>read-only projection</strong>; never recomputed from orders
    /// (square.md). <c>null</c> until the first pull.
    /// </summary>
    public int? SeatsRemaining { get; set; }

    /// <summary>Square's record of whether it is sold out. A <strong>read-only projection</strong>.</summary>
    public bool SoldOut { get; set; }

    /// <summary>Whether Square has told us anything about this session's money or seats yet (ADR 0037).</summary>
    public bool HasSquareRecord => PriceCents is not null || SeatsRemaining is not null;
}
