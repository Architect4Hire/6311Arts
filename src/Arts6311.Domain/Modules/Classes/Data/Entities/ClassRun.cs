namespace Arts6311.Domain.Modules.Classes.Data.Entities;

/// <summary>
/// One run of a class (ADR 0044): the same offering taught over one period by one instructor — "Intro to Screen
/// Printing, Saturdays this fall, with Gwen". A seat is bought in the run, not in a sitting, so the run is what Square
/// sells: its own variation, with its own SKU, starting price and seat count (ADR 0032, 0037).
/// </summary>
public sealed class ClassRun
{
    public Guid Id { get; set; }

    public Guid ClassOfferingId { get; set; }

    /// <summary>What staff and the site call this run when the class has several: "Fall 2026", "Sunday mornings".</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Who teaches it: an instructor in the Instructors module, by a <c>Restrict</c> foreign key, so an instructor with
    /// a run cannot vanish from under it (ADR 0043, 0044). Archive is how an instructor is retired. Required to publish.
    /// </summary>
    public Guid? InstructorId { get; set; }

    /// <summary>How it meets: "Saturdays", "Wednesdays, 5 weeks".</summary>
    public string? Cadence { get; set; }

    /// <summary>
    /// The IANA zone its sessions' times are shown in (backend.md). The gallery's own zone unless staff change it, so
    /// an online run can be advertised in another.
    /// </summary>
    public string DisplayTimeZone { get; set; } = string.Empty;

    /// <summary>
    /// What it is sold under. <strong>CMS-owned</strong> and unique across the whole CMS, which the shared
    /// <c>CatalogSkus</c> registry enforces (ADR 0037). Suggested on create, editable by staff.
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// How many seats it has. <strong>CMS-owned</strong> (ADR 0032): the starting value creates the Square seat count,
    /// and a later change stages a seat adjustment by the difference (5.9).
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// What one seat costs when the first push creates the Square variation (ADR 0032). A draft field until then, and
    /// frozen once Square has reported money or seats for this run: from that point a price change is a command
    /// (5.4a). Never written by an inbound pull — <see cref="PriceCents"/> is what Square's record lands in.
    /// </summary>
    public long? StartingPriceCents { get; set; }

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

    /// <summary>The sittings, in start order. A seat in the run is a seat in every one of them.</summary>
    public List<ClassSession> Sessions { get; set; } = [];

    /// <summary>Whether Square has told us anything about this run's money or seats yet (ADR 0037).</summary>
    public bool HasSquareRecord => PriceCents is not null || SeatsRemaining is not null;
}
