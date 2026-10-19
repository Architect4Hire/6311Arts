using System.Diagnostics;
using Arts6311.Domain.Managers.Outbox;

namespace Arts6311.Worker;

/// <summary>
/// Drains the outbox: one dispatch pass at startup, then one every <see cref="OutboxPolicy.PollingInterval"/>, each in
/// a fresh DI scope (so a fresh <c>ArtsDbContext</c>). A pass that throws is logged and the next tick tries again: a
/// database outage must not stop the host.
/// </summary>
internal sealed partial class OutboxDispatcherHostedService(
    IServiceScopeFactory scopes,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcherHostedService> logger) : BackgroundService
{
    internal const string PollActivityName = "outbox.poll";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(OutboxPolicy.PollingInterval, timeProvider);

        try
        {
            do
            {
                await DispatchOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown. An interrupted message stays Leased and its lease brings it back.
        }
    }

    internal async Task<OutboxDispatchSummary?> DispatchOnceAsync(CancellationToken stoppingToken)
    {
        // Every pass reads the outbox, so an idle Worker would put two SQL spans in a new trace every five seconds.
        // The pass runs under a deliberately unrecorded activity instead: parent-based sampling (the OpenTelemetry
        // default) then drops every child span the read and the claims create. Claimed messages are unaffected —
        // the dispatcher parents each one's span on the request that enqueued it, not on this activity.
        using var poll = new Activity(PollActivityName);
        poll.ActivityTraceFlags = ActivityTraceFlags.None;
        poll.Start();

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var summary = await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>()
                .DispatchDueAsync(stoppingToken);

            if (summary.Claimed > 0)
            {
                LogDispatched(summary.Claimed, summary.Completed, summary.Retrying, summary.Poisoned);
            }

            return summary;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                // Shutdown that surfaced as something else — SqlClient reports a cancelled command as a SqlException.
                // Not a failure, so not an Error on every stop.
                LogPassInterrupted();
            }
            else
            {
                // Infrastructure only (the database, the scope): handler failures are recorded per message, not thrown.
                LogPassFailed(ex);
            }

            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outbox pass interrupted by shutdown.")]
    private partial void LogPassInterrupted();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Outbox pass claimed {Claimed}: {Completed} completed, {Retrying} retrying, {Poisoned} poisoned.")]
    private partial void LogDispatched(int claimed, int completed, int retrying, int poisoned);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox pass failed; retrying on the next tick.")]
    private partial void LogPassFailed(Exception exception);
}
