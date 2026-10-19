using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arts6311.Tests.Api;

/// <summary>
/// A rejected token is still a credential, so it must never reach the logs (auth.md "Token storage": tokens never
/// appear in logs).
/// </summary>
/// <remarks>
/// Our own handler logs the exception *type* only. The framework's <c>JwtBearerHandler</c> also logs the failure,
/// and IdentityModel's messages quote claim values and sometimes the token, so the guarantee would otherwise rest
/// on <c>appsettings.json</c> keeping <c>Microsoft.AspNetCore</c> at Warning — one configuration change away from
/// leaking. These tests capture everything, at Trace, for every category.
/// </remarks>
public sealed class InternalTokenLoggingTests
{
    [Fact]
    public async Task ARejectedTokenIsNeverWrittenToTheLogs()
    {
        var sink = new CapturingLoggerProvider();
        using var api = new ApiFactory(configureServices: services => services.AddLogging(logging =>
        {
            // Everything, from every category: the point is to catch a leak the production log levels hide.
            logging.AddProvider(sink);
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Trace);
        }));
        using var client = api.CreateJsonClient();

        var token = api.Tokens.Token(audience: "6311-someone-else", roles: ["Admin"]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cms/ops/ping");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        await client.SendAsync(request, TestContext.Current.CancellationToken);

        var logged = sink.Text();

        Assert.DoesNotContain(token, logged, StringComparison.Ordinal);

        // Also none of its parts: a header or payload segment on its own is still token material.
        foreach (var segment in token.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.DoesNotContain(segment, logged, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheRejectionIsStillRecordedSoAFailureIsDiagnosable()
    {
        // The counterpart: "no token in the logs" must not have been achieved by logging nothing at all.
        var sink = new CapturingLoggerProvider();
        using var api = new ApiFactory(configureServices: services => services.AddLogging(logging =>
        {
            logging.AddProvider(sink);
            logging.SetMinimumLevel(LogLevel.Trace);
        }));
        using var client = api.CreateJsonClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/cms/ops/ping");
        request.Headers.TryAddWithoutValidation(
            "Authorization", $"Bearer {api.Tokens.Token(audience: "6311-someone-else", roles: ["Admin"])}");
        await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains("SecurityTokenInvalidAudienceException", sink.Text(), StringComparison.Ordinal);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> lines = [];

        public string Text()
        {
            lock (lines)
            {
                return string.Join('\n', lines);
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, lines);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                // The formatted message, the exception and its inner chain: a leak anywhere in that is a leak.
                var text = $"{category} {logLevel} {formatter(state, exception)} {exception}";
                lock (lines)
                {
                    lines.Add(text);
                }
            }
        }
    }
}
