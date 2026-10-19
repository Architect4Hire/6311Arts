using System.Diagnostics;
using Arts6311.Domain.Managers.Persistence;

namespace Arts6311.MigrationService;

/// <summary>
/// Applies every migration target, then every seeder, then stops the host. Dependents in the AppHost use
/// <c>WaitForCompletion(migrations)</c>, and the exit code (<see cref="MigrationRunState"/>) is what tells them
/// whether it succeeded. Ported from creatorpantry.
/// </summary>
internal sealed partial class MigrationWorker(
    IServiceProvider services,
    IHostEnvironment environment,
    IHostApplicationLifetime lifetime,
    MigrationRunState state,
    ILogger<MigrationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ServiceDefaults traces the ActivitySource named after the application.
        using var source = new ActivitySource(environment.ApplicationName);
        using var activity = source.StartActivity("Migrating database", ActivityKind.Client);

        try
        {
            await using var scope = services.CreateAsyncScope();

            var targets = scope.ServiceProvider.GetServices<IMigrationTarget>().ToList();
            if (targets.Count == 0)
            {
                LogNoTargets();
            }

            foreach (var target in targets)
            {
                LogApplying(target.Name);
                await target.MigrateAsync(stoppingToken);
                LogApplied(target.Name);
            }

            // Seeders run only once every schema is current, and must be idempotent.
            foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
            {
                LogSeeding(seeder.Name);
                await seeder.SeedAsync(stoppingToken);
            }

            state.Outcome = MigrationOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogCancelled();
            activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
            state.Outcome = MigrationOutcome.Failed;
        }
#pragma warning disable CA1031 // Any failure must become a non-zero exit code, never an unobserved crash.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(ex);
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error);
            state.Outcome = MigrationOutcome.Failed;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "No migration targets registered; nothing to apply.")]
    private partial void LogNoTargets();

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying migrations for {MigrationTarget}.")]
    private partial void LogApplying(string migrationTarget);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations applied for {MigrationTarget}.")]
    private partial void LogApplied(string migrationTarget);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding {Seeder}.")]
    private partial void LogSeeding(string seeder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration run was cancelled before completion.")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Critical, Message = "Migration run failed.")]
    private partial void LogFailed(Exception exception);
}
