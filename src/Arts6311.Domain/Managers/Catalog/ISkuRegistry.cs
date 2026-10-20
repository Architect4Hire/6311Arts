namespace Arts6311.Domain.Managers.Catalog;

/// <summary>Who holds a SKU: the owner type and id from its <see cref="CatalogSku"/> row.</summary>
public sealed record SkuOwner(string OwnerType, Guid OwnerId);

/// <summary>
/// The SKUs in use across the CMS (ADR 0037). Reservations are <strong>staged</strong> on the ambient
/// <c>ArtsDbContext</c>, exactly as <c>IOutboxWriter.Enqueue</c> and <c>IAuditWriter.Record</c> are: the claim
/// commits in the same <c>SaveChangesAsync</c> as the row that carries the SKU, or neither does.
/// </summary>
/// <remarks>
/// A content module's DataLayer is the only caller. Business asks <see cref="OwnersAsync"/> who holds a SKU before
/// it decides, and the DataLayer stages what Business decided.
/// </remarks>
public interface ISkuRegistry
{
    /// <summary>
    /// Who currently holds each of <paramref name="skus"/>; a SKU nobody holds is absent from the answer. Read
    /// without tracking: the caller compares owners, it does not edit rows.
    /// </summary>
    Task<IReadOnlyDictionary<string, SkuOwner>> OwnersAsync(
        IReadOnlyCollection<string> skus, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="sku"/> is held by anything other than <paramref name="owner"/>.</summary>
    /// <remarks>
    /// The question a failed save asks: the unique key refused the insert, so either someone took the SKU in the
    /// meantime or the fault was something else entirely.
    /// </remarks>
    Task<bool> IsTakenAsync(string sku, SkuOwner owner, CancellationToken cancellationToken);

    /// <summary>
    /// Stages a reservation of <paramref name="sku"/> for one record, without saving. Throws for a SKU outside
    /// <see cref="SkuPolicy.IsValid"/>: the registry is the one guard on the shape, so a bad one is a defect here
    /// rather than a 500 from the database at the caller's save.
    /// </summary>
    void Reserve(string sku, string ownerType, Guid ownerId);

    /// <summary>
    /// Stages the release of <paramref name="sku"/>, without saving: the record that held it is gone, or has been
    /// given another.
    /// </summary>
    /// <remarks>
    /// The row is read first, and nothing is staged unless <paramref name="owner"/> still holds it: a SKU no row
    /// holds is not released again (which would delete nothing and fail the save as a concurrency conflict), and
    /// one another record has taken is left alone rather than deleted from under it.
    /// </remarks>
    Task ReleaseAsync(string sku, SkuOwner owner, CancellationToken cancellationToken);
}
