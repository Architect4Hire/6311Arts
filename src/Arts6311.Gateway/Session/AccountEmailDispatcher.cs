using System.Diagnostics;

namespace Arts6311.Gateway.Session;

/// <summary>
/// Drains <see cref="AccountEmailQueue"/> into the API's internal routes, after the browser has had its 202
/// (ADR 0028, 0029).
/// </summary>
/// <remarks>
/// <para>
/// One request at a time, in order. The rate limit and the cooldown have already bounded how many arrive, and the
/// API's work per request is a lookup and at most one save.
/// </para>
/// <para>
/// Each call runs under a span parented on the browser request that queued it, so the trace still runs gateway →
/// API → outbox → Worker → Mailpit even though the response went first. The span's name says which kind; nothing
/// on it carries the address.
/// </para>
/// <para>
/// A failure is logged by exception <em>type</em>, its cooldown is released, and it is dropped. There is nobody
/// left to tell — the browser was answered already, and was always going to be told the same thing — and retrying
/// here would only make a gateway with a sick API hold addresses in memory for longer. Releasing the cooldown means
/// the person's own retry goes through.
/// </para>
/// </remarks>
internal sealed partial class AccountEmailDispatcher(
    AccountEmailQueue queue,
    IServiceScopeFactory scopes,
    ISessionKeyValueStore store,
    ILogger<AccountEmailDispatcher> logger) : BackgroundService
{
    /// <summary>
    /// The gateway's own source, under the application name ServiceDefaults already exports
    /// (<c>AddSource(ApplicationName)</c>).
    /// </summary>
    private static readonly ActivitySource Source = new(typeof(AccountEmailDispatcher).Assembly.GetName().Name!);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken))
        {
            using var activity = Source.StartActivity(SpanName(request.Kind), ActivityKind.Internal, request.Origin);

            try
            {
                // A scope per call: the typed HttpClient behind IIdentityApiClient is transient, and holding one in
                // this singleton would pin its handler for the life of the process.
                await using var scope = scopes.CreateAsyncScope();
                var identity = scope.ServiceProvider.GetRequiredService<IIdentityApiClient>();

                await (request.Kind switch
                {
                    AccountEmailKind.PasswordReset =>
                        identity.RequestPasswordResetAsync(request.Email, stoppingToken),
                    AccountEmailKind.EmailConfirmation =>
                        identity.RequestEmailConfirmationAsync(request.Email, stoppingToken),
                    _ => throw new InvalidOperationException($"Unknown account email kind {request.Kind}."),
                });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                LogRequestFailed(logger, request.Kind, exception.GetType().Name);
                await ReleaseCooldownAsync(request.CooldownKey, stoppingToken);
            }
        }
    }

    private static string SpanName(AccountEmailKind kind) => kind switch
    {
        AccountEmailKind.PasswordReset => "bff.forgot-password dispatch",
        AccountEmailKind.EmailConfirmation => "bff.resend-confirmation dispatch",
        _ => "bff.account-email dispatch",
    };

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
        Message = "A failed account email request's cooldown could not be released ({ExceptionType}).")]
    private static partial void LogCooldownNotReleased(ILogger logger, string exceptionType);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An account email request ({Kind}) could not be passed to the internal API ({ExceptionType}).")]
    private static partial void LogRequestFailed(ILogger logger, AccountEmailKind kind, string exceptionType);
}
