using Microsoft.EntityFrameworkCore;

namespace Arts6311.MigrationService;

/// <summary>A database whose schema the migration service brings up to date before dependents start.</summary>
internal interface IMigrationTarget
{
    string Name { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}

internal sealed class DbContextMigrationTarget<TContext>(TContext context) : IMigrationTarget
    where TContext : DbContext
{
    public string Name => typeof(TContext).Name;

    // The execution strategy (retry-on-failure, from the Aspire integration) retries transient SQL failures
    // around the whole migration run.
    public Task MigrateAsync(CancellationToken cancellationToken) =>
        context.Database.CreateExecutionStrategy()
            .ExecuteAsync(context, static (ctx, token) => ctx.Database.MigrateAsync(token), cancellationToken);
}

/// <summary>The outcome of the run, read by <c>Program</c> to choose the process exit code.</summary>
internal sealed class MigrationRunState
{
    public MigrationOutcome Outcome { get; set; } = MigrationOutcome.NotRun;

    public int ExitCode => Outcome == MigrationOutcome.Succeeded ? 0 : 1;
}

internal enum MigrationOutcome
{
    NotRun,
    Succeeded,
    Failed,
}

internal static class MigrationServiceCollectionExtensions
{
    /// <summary>Registers the migration host: the one-shot worker and its run outcome.</summary>
    public static IServiceCollection AddMigrationHost(this IServiceCollection services)
    {
        services.AddSingleton<MigrationRunState>();
        services.AddHostedService<MigrationWorker>();
        return services;
    }

    /// <summary>Adds <typeparamref name="TContext"/> as a migration target. The context itself is registered separately.</summary>
    public static IServiceCollection AddDbContextMigration<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IMigrationTarget, DbContextMigrationTarget<TContext>>();
        return services;
    }
}
