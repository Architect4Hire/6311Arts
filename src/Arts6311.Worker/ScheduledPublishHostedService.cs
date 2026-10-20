using System.Diagnostics;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Time;

namespace Arts6311.Worker;

/// <summary>What one pass did, across every content type: for the pass's span, its log line and its tests.</summary>
/// <param name="Failed">
/// Units that threw. Each is logged and the pass carries on, so one record that cannot be published never starves
/// the ones behind it (ADR 0036).
/// </param>
internal sealed record ScheduledPublishSummary(
    int Due, int Published, int Unscheduled, int Lost, int Skipped, int Failed = 0);

/// <summary>
/// Publishes what staff scheduled: one pass at startup, then one every
/// <see cref="ScheduledPublishPolicy.Interval"/>, each in a fresh DI scope (so a fresh <c>ArtsDbContext</c>). A
/// pass that throws is logged and the next tick tries again — a database outage must not stop the host (ADR 0036).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two replicas are safe without a lease.</strong> Each unit re-reads its record and saves it with the row
/// version it just read, which is a concurrency token: of two Workers publishing one record, one save succeeds and
/// the other is told it lost (add-background-job step 2). Nothing is claimed up front, so a Worker that dies
/// mid-pass leaves no held work behind — the next pass reads the due list again, which is also the crash-recovery
/// path.
/// </para>
/// <para>
/// <strong>Each unit gets its own DI scope,</strong> and so its own <c>ArtsDbContext</c>. A unit that throws after
/// staging entities would otherwise leave them tracked, and the next unit's save would re-attempt the failed write
/// and fail with it — one bad record taking down the rest of the pass, which is exactly what the per-unit catch
/// below exists to prevent (ADR 0036). The due lists are read in a scope of their own, before any of it.
/// </para>
/// <para>
/// <strong>Publishing goes through each module's facade</strong> (<see cref="IScheduledPublisher"/>), so this job
/// touches no DbContext, repository or gateway, and each publish stages its own revalidation in its own save
/// (add-background-job step 3).
/// </para>
/// </remarks>
internal sealed partial class ScheduledPublishHostedService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<ScheduledPublishHostedService> logger) : BackgroundService
{
    internal const string PassActivityName = "publishing.scheduled-pass";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScheduledPublishPolicy.Interval, timeProvider);

        try
        {
            do
            {
                await PublishDueAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown. Whatever was still due is published by the next pass, here or on another replica.
        }
    }

    /// <returns>What the pass did, or <c>null</c> when it failed (logged; the next tick tries again).</returns>
    internal async Task<ScheduledPublishSummary?> PublishDueAsync(CancellationToken stoppingToken)
    {
        // A root span of its own, once a minute, carrying the counts.
        using var activity = WorkerDiagnostics.Source.StartActivity(PassActivityName);
        var published = 0;
        var unscheduled = 0;
        var lost = 0;
        var skipped = 0;
        var failed = 0;
        var due = 0;

        try
        {
            DateTimeOffset asOf;
            List<(string ContentType, IReadOnlyList<Guid> Ids)> dueByType = [];

            await using (var reading = scopes.CreateAsyncScope())
            {
                asOf = reading.ServiceProvider.GetRequiredService<IClock>().UtcNow;

                foreach (var publisher in reading.ServiceProvider.GetServices<IScheduledPublisher>())
                {
                    var ids = await publisher.ListDueAsync(asOf, ScheduledPublishPolicy.BatchSize, stoppingToken);
                    due += ids.Count;
                    if (ids.Count > 0)
                    {
                        dueByType.Add((publisher.ContentType, ids));
                    }
                }
            }

            foreach (var (contentType, ids) in dueByType)
            {
                foreach (var id in ids)
                {
                    // Honoured between units, never inside one: a unit is one save, so nothing is half-applied.
                    stoppingToken.ThrowIfCancellationRequested();

                    ScheduledPublishOutcome outcome;
                    try
                    {
                        await using var unit = scopes.CreateAsyncScope();
                        outcome = await PublisherOf(unit, contentType).PublishDueAsync(id, asOf, stoppingToken);
                    }
                    catch (Exception failure) when (failure is not OperationCanceledException)
                    {
                        // One record that cannot be published must not starve the rest: the due list is ordered by
                        // time, so without this a permanently failing record would be retried first every minute
                        // and nothing behind it would ever run (ADR 0036). Only the type is logged, as everywhere.
                        failed++;
                        LogUnitFailed(contentType, id, failure.GetType().Name);
                        continue;
                    }

                    switch (outcome)
                    {
                        case ScheduledPublishOutcome.Published:
                            published++;
                            LogPublished(contentType, id);
                            break;
                        case ScheduledPublishOutcome.Unscheduled:
                            unscheduled++;
                            LogUnscheduled(contentType, id);
                            break;
                        case ScheduledPublishOutcome.Lost:
                            lost++;
                            break;
                        default:
                            skipped++;
                            break;
                    }
                }
            }

            var summary = new ScheduledPublishSummary(due, published, unscheduled, lost, skipped, failed);
            Record(activity, summary);

            if (due > 0)
            {
                LogPass(due, published, unscheduled, lost, skipped, failed);
            }

            return summary;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            Record(activity, new ScheduledPublishSummary(due, published, unscheduled, lost, skipped, failed));

            if (stoppingToken.IsCancellationRequested)
            {
                // Shutdown that surfaced as something else — SqlClient reports a cancelled command as a SqlException.
                LogPassInterrupted();
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                LogPassFailed(ex);
            }

            return null;
        }
    }

    /// <summary>This scope's publisher for <paramref name="contentType"/>, as the reading pass named it.</summary>
    private static IScheduledPublisher PublisherOf(AsyncServiceScope scope, string contentType) =>
        scope.ServiceProvider.GetServices<IScheduledPublisher>()
            .First(publisher => publisher.ContentType == contentType);

    private static void Record(Activity? activity, ScheduledPublishSummary summary)
    {
        activity?.SetTag("publishing.due", summary.Due);
        activity?.SetTag("publishing.published", summary.Published);
        activity?.SetTag("publishing.unscheduled", summary.Unscheduled);
        activity?.SetTag("publishing.lost", summary.Lost);
        activity?.SetTag("publishing.skipped", summary.Skipped);
        activity?.SetTag("publishing.failed", summary.Failed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Published scheduled {ContentType} {Id}.")]
    private partial void LogPublished(string contentType, Guid id);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Scheduled {ContentType} {Id} is a draft again: publishing it is no longer possible. See its audit trail.")]
    private partial void LogUnscheduled(string contentType, Guid id);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Publishing scheduled {ContentType} {Id} failed ({ExceptionType}); the pass carried on.")]
    private partial void LogUnitFailed(string contentType, Guid id, string exceptionType);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Scheduled publish pass found {Due} due: {Published} published, {Unscheduled} unscheduled, "
            + "{Lost} taken by someone else, {Skipped} skipped, {Failed} failed.")]
    private partial void LogPass(int due, int published, int unscheduled, int lost, int skipped, int failed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scheduled publish pass interrupted by shutdown.")]
    private partial void LogPassInterrupted();

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled publish pass failed; retrying on the next tick.")]
    private partial void LogPassFailed(Exception exception);
}
