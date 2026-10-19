namespace Arts6311.Domain.Managers.Correlation;

/// <summary>
/// The current request's or operation's correlation id, which a DataLayer passes to <c>IOutboxWriter.Enqueue</c>
/// and <c>AuditEntry</c> so one trace runs from CMS save → message → Worker → Square (ADR 0019).
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// The current <see cref="System.Diagnostics.Activity"/>'s W3C trace id as a <see cref="Guid"/> (its
    /// <c>"N"</c> format is the trace id's hex), or, with no W3C activity, one id fixed for this DI scope.
    /// Never <see cref="Guid.Empty"/>.
    /// </summary>
    Guid Current { get; }
}
