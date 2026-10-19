using Arts6311.Domain.Managers.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// An in-memory SQLite <see cref="ArtsDbContext"/> with the same options every host applies
/// (<see cref="ArtsDbContextOptions.Configure"/>), for kernel behaviour that does not depend on SQL Server.
/// The database lives as long as this object holds the connection open.
/// </summary>
internal sealed class SqliteArtsDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private SqliteArtsDatabase(SqliteConnection connection) => this.connection = connection;

    public static async Task<SqliteArtsDatabase> CreateAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);
        var database = new SqliteArtsDatabase(connection);
        await using var context = database.NewContext();
        await context.Database.EnsureCreatedAsync(cancellationToken);
        return database;
    }

    public ArtsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ArtsDbContext>().UseSqlite(connection);
        ArtsDbContextOptions.Configure(options);
        return new ArtsDbContext(options.Options);
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}
