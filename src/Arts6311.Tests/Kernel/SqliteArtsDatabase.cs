using Arts6311.Domain.Managers.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Arts6311.Tests.Kernel;

/// <summary>
/// A SQLite <see cref="ArtsDbContext"/> with the same options every host applies
/// (<see cref="ArtsDbContextOptions.Configure"/>), for kernel behaviour that does not depend on SQL Server, plus
/// <see cref="SqliteModelCustomizer"/>, the harness's one model adjustment.
/// </summary>
/// <remarks>
/// <see cref="CreateAsync"/> is in-memory on one shared connection: the database lives as long as this object holds
/// it open. <see cref="CreateFileAsync"/> is a temporary file with a connection per context, like requests, for
/// tests where two contexts must genuinely contend (SQLite's locking stands in for SQL Server's).
/// </remarks>
internal sealed class SqliteArtsDatabase : IAsyncDisposable
{
    private readonly SqliteConnection? connection;
    private readonly string? path;
    private readonly Action<DbContextOptionsBuilder<ArtsDbContext>>? configure;

    private SqliteArtsDatabase(
        SqliteConnection? connection, string? path, Action<DbContextOptionsBuilder<ArtsDbContext>>? configure = null) =>
        (this.connection, this.path, this.configure) = (connection, path, configure);

    /// <param name="configure">
    /// Applied to every context after the harness's own options, for a test that needs a different model — for
    /// example a table that references a module's entity (a later <c>ReplaceService</c> wins).
    /// </param>
    public static async Task<SqliteArtsDatabase> CreateAsync(
        CancellationToken cancellationToken, Action<DbContextOptionsBuilder<ArtsDbContext>>? configure = null)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken);
        return await EnsureCreatedAsync(new SqliteArtsDatabase(connection, path: null, configure), cancellationToken);
    }

    public static Task<SqliteArtsDatabase> CreateFileAsync(CancellationToken cancellationToken) =>
        EnsureCreatedAsync(
            new SqliteArtsDatabase(connection: null, Path.Combine(Path.GetTempPath(), $"arts6311-{Guid.NewGuid():N}.db")),
            cancellationToken);

    /// <param name="interceptors">Test-only interceptors, added after the hosts' own.</param>
    public ArtsDbContext NewContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ArtsDbContext>();
        if (connection is not null)
        {
            builder.UseSqlite(connection);
        }
        else
        {
            builder.UseSqlite($"Data Source={path}");
        }

        builder.ReplaceService<IModelCustomizer, SqliteModelCustomizer>();
        configure?.Invoke(builder);
        ArtsDbContextOptions.Configure(builder);
        builder.AddInterceptors(interceptors);
        return new ArtsDbContext(builder.Options);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
            return;
        }

        SqliteConnection.ClearAllPools();
        File.Delete(path!);
    }

    private static async Task<SqliteArtsDatabase> EnsureCreatedAsync(SqliteArtsDatabase database, CancellationToken cancellationToken)
    {
        await using var context = database.NewContext();
        await context.Database.EnsureCreatedAsync(cancellationToken);
        return database;
    }
}
