using Arts6311.Domain.Managers.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// The committed migrations describe the current model. Fails when an entity or configuration changed without
/// `dotnet ef migrations add` (CLAUDE.md "Migrations"). Needs no database: it compares the model to the snapshot.
/// </summary>
public sealed class MigrationSyncTests
{
    [Fact]
    public void TheModelHasNoChangesMissingFromTheMigrations()
    {
        var options = new DbContextOptionsBuilder<ArtsDbContext>().UseSqlServer();
        ArtsDbContextOptions.Configure(options);
        using var context = new ArtsDbContext(options.Options);

        Assert.False(context.Database.HasPendingModelChanges(), "Run `dotnet ef migrations add <Name>` (CLAUDE.md).");
    }
}
