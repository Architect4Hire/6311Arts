using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// Stores every <c>DateTimeOffset</c> as UTC ticks under SQLite and drops SQL Server collations — test-harness
/// workarounds (the first ported from creatorpantry); the production mappings stay native.
/// </summary>
/// <remarks>
/// <para>
/// SQLite keeps <c>DateTimeOffset</c> as text, and EF Core will not translate comparisons or <c>ORDER BY</c> over it,
/// so a query like the idempotency sweep's <c>ExpiresAt &lt;= now</c> throws before reaching a row. Integer ticks
/// compare and order exactly like the instants they encode.
/// </para>
/// <para>
/// The cost: the original offset is not recoverable — a <c>+02:00</c> value reads back as the same instant at
/// <c>+00:00</c>. Every timestamp this system writes comes from <c>IClock.UtcNow</c> at offset zero, so the round trip
/// is lossless for real data. Applied by walking the model, so an entity added later is covered without anyone
/// remembering to come back here.
/// </para>
/// </remarks>
internal sealed class SqliteModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    private static readonly ValueConverter<DateTimeOffset, long> ToUtcTicks =
        new(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> ToNullableUtcTicks =
        new(value => value == null ? null : value.Value.UtcTicks,
            ticks => ticks == null ? null : new DateTimeOffset(ticks.Value, TimeSpan.Zero));

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            // SQL Server collation names (Latin1_General_BIN2) do not exist in SQLite, which would refuse to create the
            // table. Dropping them is faithful for the binary collations the model uses: SQLite compares binary already.
            if (property.GetCollation() is not null)
            {
                property.SetCollation(null);
            }

            if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(ToUtcTicks);
            }
            else if (property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(ToNullableUtcTicks);
            }
        }
    }
}
