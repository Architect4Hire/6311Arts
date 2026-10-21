using Arts6311.Domain.Modules.Classes.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Classes.Data.Configurations;

internal sealed class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
{
    public void Configure(EntityTypeBuilder<ClassSession> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ClassSessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        // The site's "what's on" and the admin's grid: a run's sittings in start order, and every upcoming session
        // across runs.
        builder.HasIndex(session => new { session.ClassRunId, session.StartsAt })
            .HasDatabaseName("IX_ClassSessions_ClassRunId_StartsAt");

        builder.HasIndex(session => session.StartsAt)
            .HasDatabaseName("IX_ClassSessions_StartsAt");
    }
}
