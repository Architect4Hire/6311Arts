using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Managers.Idempotency;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    /// <summary>Case- and accent-sensitive, ordinal comparison on SQL Server and Azure SQL.</summary>
    internal const string ScopeCollation = "Latin1_General_BIN2";

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(record => record.Id);

        // ASCII (varchar), all three: IdempotencyBusiness admits only printable ASCII to each, and as nvarchar the
        // unique scope index below would be 1710 bytes, past SQL Server's 1700-byte nonclustered key limit — an insert
        // of long-enough values would fail. As varchar it is 855.
        //
        // Binary collation, all three: the database default is case-insensitive, under which the keys "Abc" and "abc"
        // would share a scope and one would replay (or be refused for) the other's request. A scope matches only
        // byte-for-byte. (The SQLite test harness drops collations; SQLite compares binary by default.)
        builder.Property(record => record.Subject).IsRequired().HasMaxLength(IdempotencyPolicy.SubjectMaxLength)
            .IsUnicode(false).UseCollation(ScopeCollation);
        builder.Property(record => record.Operation).IsRequired().HasMaxLength(IdempotencyPolicy.OperationMaxLength)
            .IsUnicode(false).UseCollation(ScopeCollation);
        builder.Property(record => record.Key).IsRequired().HasMaxLength(IdempotencyPolicy.KeyMaxLength)
            .IsUnicode(false).UseCollation(ScopeCollation);
        builder.Property(record => record.FingerprintHash).IsRequired().HasMaxLength(32).IsFixedLength();
        builder.Property(record => record.ResultJson).IsRequired();
        builder.Property(record => record.CreatedAt).IsRequired();
        builder.Property(record => record.ExpiresAt).IsRequired();

        // The scope is never a bare key. A concurrent insert of the same scope waits on this index until the
        // first transaction commits, which is what serializes duplicate requests.
        builder.HasIndex(record => new { record.Subject, record.Operation, record.Key })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyRecords_Scope");

        // For the Worker's expired-record sweep.
        builder.HasIndex(record => record.ExpiresAt).HasDatabaseName("IX_IdempotencyRecords_ExpiresAt");
    }
}
