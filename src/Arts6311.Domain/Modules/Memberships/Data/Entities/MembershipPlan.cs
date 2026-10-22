using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Memberships.Managers;

namespace Arts6311.Domain.Modules.Memberships.Data.Entities;

/// <summary>
/// One membership tier the centre sells (content.md "Content types"): its name, what it saves a member on classes,
/// and the perks the <c>/memberships</c> page lists. <strong>Square-synced</strong> (ADR 0005, 0040): the CMS owns
/// this copy and the figures the first push creates the plan with; Square owns the record of money and billing.
/// </summary>
/// <remarks>
/// A plan has <strong>no page of its own</strong> (ADR 0040): the site shows every published plan on
/// <c>/memberships</c>, so the slug is a stable key — for the member signup, the Square plan's CMS id attribute and
/// the page's own anchors — rather than an address. Nothing redirects when it changes, because no one could have
/// bookmarked it.
/// </remarks>
public sealed class MembershipPlan : ISoftDeletable
{
    public Guid Id { get; set; }

    /// <summary>What members call the tier: “Gray”, “Black”, “Blue”.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unique among plans. A stable key, not an address (ADR 0040).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>What a member of this tier saves on every class and workshop, as whole percent.</summary>
    public int DiscountPercent { get; set; }

    /// <summary>
    /// Whether the <c>/memberships</c> page highlights this tier. A plain flag: how many tiers are highlighted at
    /// once is the site's business, not an invariant here (ADR 0040).
    /// </summary>
    public bool Featured { get; set; }

    /// <summary>Where it comes among the tiers on the page, from 1, cheapest first by convention.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>What the tier includes, in the order someone chose. The page lists them as they come.</summary>
    public List<MembershipPlanPerk> Perks { get; set; } = [];

    /// <summary>
    /// What it costs when the first push creates the Square subscription plan (ADR 0005). A draft field until then,
    /// and frozen once Square has reported money or a cadence: from that point price is Square's.
    /// </summary>
    public long? StartingPriceCents { get; set; }

    /// <summary>
    /// How often it bills when the first push creates the Square plan (ADR 0005). Frozen with
    /// <see cref="StartingPriceCents"/>, after which <see cref="Cadence"/> is the record.
    /// </summary>
    public MembershipCadence StartingCadence { get; set; } = MembershipCadence.Annual;

    /// <summary>
    /// Square's record of what the plan bills, in cents. A <strong>read-only projection</strong>, written only by
    /// the inbound pull through the <c>SquareSync</c> facade (square.md, ADR 0005). <c>null</c> until Square has
    /// reported one.
    /// </summary>
    public long? PriceCents { get; set; }

    /// <summary>
    /// Square's record of how often it bills, in Square's own words (<c>ANNUAL</c>, <c>MONTHLY</c>, …). A
    /// <strong>read-only projection</strong>, and a string because Square has more cadences than the CMS offers
    /// (ADR 0040). <c>null</c> until the first pull.
    /// </summary>
    public string? Cadence { get; set; }

    /// <summary>The WooCommerce membership product this plan was imported from (prompt 4.x). Set by the importer only.</summary>
    public long? LegacyWordPressId { get; set; }

    public PublishStatus Status { get; set; }

    /// <summary>When it was first published. Kept through an unpublish, as every content type keeps it.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>When the scheduled publisher should publish it (ADR 0036). UTC.</summary>
    public DateTimeOffset? ScheduledFor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When staff deleted the membership plan, or <c>null</c> while it is live (ADR 0052). A deleted record is invisible
    /// to every query — the site included — and only an Admin's restore brings it back, as a Draft.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The user id behind the delete. <c>null</c> while the record is live.</summary>
    public string? DeletedBy { get; set; }

    /// <summary>The user id behind the last write. The importer compares it to tell staff edits from its own (content.md).</summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// The optimistic-concurrency token, rotated by every staff write (a change to the perks too) and carried as
    /// the <c>ETag</c>.
    /// </summary>
    public Guid RowVersion { get; set; }

    /// <summary>Whether Square has told us anything about this plan's money or billing yet (ADR 0005, 0040).</summary>
    public bool HasSquareRecord => PriceCents is not null || Cadence is not null;
}
