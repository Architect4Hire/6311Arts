namespace Arts6311.Domain.Managers.Catalog;

/// <summary>
/// One SKU in use in the CMS, and what holds it (ADR 0037). The table is the shared kernel's because a SKU is
/// unique across content types — a class session and a shop item cannot share one — and no single module may own a
/// rule about another's rows (<c>backend.md</c>).
/// </summary>
/// <remarks>
/// The owner is named by a <strong>string</strong> type plus an id rather than a foreign key: the kernel depends on
/// no module, and no one table can hold a key into several. The owning module stages the row through
/// <see cref="ISkuRegistry"/> in the same save as the row that carries the SKU, so a reservation never outlives a
/// write that failed.
/// </remarks>
public sealed class CatalogSku
{
    /// <summary>The SKU itself, and the primary key: the database is what makes a SKU unique across the CMS.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Which kind of record holds it, from <see cref="CatalogOwnerTypes"/>.</summary>
    public string OwnerType { get; set; } = string.Empty;

    /// <summary>The record that holds it. Unique together with <see cref="OwnerType"/>: one SKU per record.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>When it was reserved. For the audit trail's benefit; nothing reads it to make a decision.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
