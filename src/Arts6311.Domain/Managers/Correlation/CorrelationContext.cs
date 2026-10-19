using System.Diagnostics;

namespace Arts6311.Domain.Managers.Correlation;

internal sealed class CorrelationContext : ICorrelationContext
{
    // Used only when nothing is tracing (a test, a CLI with no listener): everything staged in one scope still
    // shares one id, so the rows it wrote can be found together.
    private readonly Guid fallback = Guid.NewGuid();

    public Guid Current => Activity.Current is { } activity && activity.TraceId != default
        ? FromTraceId(activity.TraceId)
        : fallback;

    /// <summary>
    /// A W3C trace id is 16 bytes, exactly a GUID, so nothing is lost. Read big-endian so the GUID's <c>"N"</c>
    /// format equals the trace id's hex string: the dashboard's trace search and a row's CorrelationId match.
    /// </summary>
    internal static Guid FromTraceId(ActivityTraceId traceId)
    {
        Span<byte> bytes = stackalloc byte[16];
        traceId.CopyTo(bytes);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>The inverse of <see cref="FromTraceId"/>.</summary>
    internal static ActivityTraceId ToTraceId(Guid correlationId)
    {
        Span<byte> bytes = stackalloc byte[16];
        correlationId.TryWriteBytes(bytes, bigEndian: true, out _);
        return ActivityTraceId.CreateFromBytes(bytes);
    }
}
