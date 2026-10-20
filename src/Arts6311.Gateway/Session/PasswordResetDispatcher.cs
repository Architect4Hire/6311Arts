using System.Diagnostics;

namespace Arts6311.Gateway.Session;

/// <summary>
/// Drains <see cref="PasswordResetQueue"/> into the API's internal reset route, after the browser has had its 202
/// (ADR 0028).
/// </summary>
/// <remarks>
/// <para>
/// One request at a time, in order. The rate limit and the cooldown have already bounded how many arrive, and the
/// API's work per request is a lookup and at most one save.
/// </para>
/// <para>
/// Each call runs under a span parented on the browser request that queued it, so the trace still runs gateway →
/// API → outbox → Worker → Mailpit even though the response went first. The span carries no address.
/// </para>
/// <para>
/// A failure is logged by exception <em>type</em>, its cooldown is released, and it is dropped. There is nobody
/// left to tell — the browser was answered already, and was always going to be told the same thing — and retrying
/// here would only make a gateway with a sick API hold addresses in memory for longer. Releasing the cooldown means
/// the person's own retry goes through.
/// </para>
/// </remarks>
internal sealed partial class PasswordResetDispatcher(
    PasswordResetQueue queue,
    IServiceScopeFactory scopes,
    ISessionKeyValueStore store,
    ILogger<PasswordResetDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// The gateway's own source, under the application name ServiceDefaults already exports
    /// (<c>AddSource(ApplicationName)</c>).
    /// </summary>
    private static readonly ActivitySource Source = new(typeof(PasswordResetDispatcher).Assembly.GetName().Name!);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var activity = Source.StartActivity(
                "bff.forgot-password dispatch", ActivityKind.Internal, request.Origin);

            try
            {
                // A scope per call: the typed HttpClient behind IIdentityApiClient is transient, and holding one in
                // this singleton would pin its handler for the life of the process.
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IIdentityApiClient>()
                    .RequestPasswordResetAsync(request.Email, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                LogRequestFailed(logger, exception.GetType().Name);
                await ReleaseCooldownAsync(request.CooldownKey, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Gives the cooldown back after a failed call, so the person's retry goes through instead of being dropped for
    /// the rest of the window. The browser never sees the cooldown, so releasing it reveals nothing (ADR 0028).
    /// </summary>
    private async Task ReleaseCooldownAsync(string key, CancellationToken stoppingToken)
    {
        try
        {
            await store.RemoveAsync(key, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Redis is down too. The key expires on its own; the retry waits out the window.
            LogCooldownNotReleased(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A failed password reset request's cooldown could not be released ({ExceptionType}).")]
    private static partial void LogCooldownNotReleased(ILogger logger, string exceptionType);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A password reset request could not be passed to the internal API ({ExceptionType}).")]
    private static partial void LogRequestFailed(ILogger logger, string exceptionType);
}
