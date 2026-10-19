using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arts6311.Domain.Managers.Persistence;

/// <summary>
/// Builds an <see cref="ArtsDbContext"/> for <c>dotnet ef</c> at design time.
/// </summary>
/// <remarks>
/// <para>
/// Without it, <c>dotnet ef</c> starts the API host to find the context, and the API deliberately refuses to
/// start without its real configuration — the internal-token public key, the idempotency fingerprint key
/// (ADR 0021). Those are startup guarantees worth keeping, so the tool gets its own way in rather than the host
/// getting looser.
/// </para>
/// <para>
/// The connection string is a placeholder and is never opened: <c>migrations add</c> builds the model and writes
/// files, and <c>migrations script</c> only needs the provider to pick its SQL dialect. Applying migrations is
/// the MigrationService's job, with the real connection string from Aspire.
/// </para>
/// </remarks>
internal sealed class ArtsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ArtsDbContext>
{
    public ArtsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ArtsDbContext>()
            .UseSqlServer("Server=design-time;Database=arts6311;Integrated Security=true");

        // The same interceptors every host applies, so the model the tool sees is the model that runs.
        ArtsDbContextOptions.Configure(options);

        return new ArtsDbContext(options.Options);
    }
}
