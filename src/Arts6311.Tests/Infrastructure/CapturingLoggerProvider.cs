using Microsoft.Extensions.Logging;

namespace Arts6311.Tests.Infrastructure;

/// <summary>
/// Every log line from every category, formatted with its exception chain, for the tests that prove a secret is
/// never written: a rejected token, a reset token, a password, an address.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
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
