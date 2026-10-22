using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// The one place soft deletion is spelled (ADR 0052): the query filter that hides a deleted row, the index filter
/// that lets a live row reuse a deleted one's slug, and the stamping a delete and a restore share.
/// </summary>
public static class SoftDelete
{
    /// <summary>
    /// The SQL filter for a unique index on a soft-deletable table. A unique index is a database constraint and
    /// knows nothing of the query filter, so without this a deleted record would keep its slug (or SKU) for ever
    /// and releasing them on delete — the point of the exercise — would fail at the next save.
    /// </summary>
    public const string LiveRowsOnly = "[DeletedAt] IS NULL";

    /// <summary>
    /// Hides deleted rows from every query on <typeparamref name="TEntity"/>. One line per entity configuration,
    /// so "a deleted record is not there" is the default rather than something 40-odd call sites have to remember
    /// (ADR 0052). A read that must see deleted rows asks for them with <c>IgnoreQueryFilters()</c>.
    /// </summary>
    public static EntityTypeBuilder<TEntity> Apply<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ISoftDeletable
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.HasQueryFilter(entity => entity.DeletedAt == null);
    }

    /// <summary>Whether the record has been deleted.</summary>
    public static bool IsDeleted(this ISoftDeletable entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return entity.DeletedAt is not null;
    }

    /// <summary>Marks the record deleted. The DataLayer stamps the rest of the bookkeeping, as it does for any write.</summary>
    public static void Stamp(ISoftDeletable entity, string actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.DeletedAt = now;
        entity.DeletedBy = actorUserId;
    }

    /// <summary>
    /// Brings the record back. Who deleted it is cleared with it: the audit trail holds that, and leaving it on the
    /// row would say the record is still deleted to anything reading the column rather than the filter.
    /// </summary>
    public static void Clear(ISoftDeletable entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.DeletedAt = null;
        entity.DeletedBy = null;
    }
}
