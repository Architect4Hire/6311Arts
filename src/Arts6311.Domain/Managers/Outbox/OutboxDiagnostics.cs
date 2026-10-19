using System.Diagnostics;

namespace Arts6311.Domain.Managers.Outbox;

/// <summary>
/// The outbox's tracing. A host that dispatches adds <see cref="ActivitySourceName"/> to its OpenTelemetry tracing;
/// ServiceDefaults cannot, because it may not reference the Domain.
/// </summary>
public static class OutboxDiagnostics
{
    public const string ActivitySourceName = "Arts6311.Outbox";

    internal static readonly ActivitySource Source = new(ActivitySourceName);
}
