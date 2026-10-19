namespace Arts6311.Domain.Managers.Persistence;

/// <summary>
/// Seed data applied by the migration service after every migration. Must be idempotent: running a seeder
/// any number of times leaves the same data.
/// </summary>
public interface IDataSeeder
{
    string Name { get; }

    Task SeedAsync(CancellationToken cancellationToken);
}
