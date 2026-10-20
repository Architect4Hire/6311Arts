namespace Arts6311.Domain.Managers.Catalog;

/// <summary>One SKU and the record it belongs to, for the registry row a DataLayer stages beside it (ADR 0037).</summary>
public sealed record SkuReservation(string Sku, Guid OwnerId);

/// <summary>
/// What one write does to the shared SKU registry: the SKUs its records now hold and the ones they let go.
/// Business decides them while it edits; staging the rows is the DataLayer's bookkeeping (backend.md).
/// </summary>
/// <remarks>
/// In the kernel beside the registry itself (ADR 0038): both Classes and Shop write these, and a module may not
/// reach into another module's types. Which <see cref="CatalogOwnerTypes"/> value the rows carry is the staging
/// DataLayer's to say, because only it knows what kind of record it is writing.
/// </remarks>
public sealed record SkuChanges(IReadOnlyList<SkuReservation> Reserve, IReadOnlyList<SkuReservation> Release)
{
    public static SkuChanges None { get; } = new([], []);
}
