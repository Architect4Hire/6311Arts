using System.Diagnostics;

namespace Arts6311.Worker;

/// <summary>Spans for the Worker's own jobs (one per pass, add-background-job "Telemetry").</summary>
internal static class WorkerDiagnostics
{
    public const string ActivitySourceName = "Arts6311.Worker";

    public static readonly ActivitySource Source = new(ActivitySourceName);
}
