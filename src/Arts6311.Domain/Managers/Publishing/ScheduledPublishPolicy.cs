namespace Arts6311.Domain.Managers.Publishing;

/// <summary>The numbers behind the Worker's scheduled publisher (ADR 0036), as <c>OutboxPolicy</c> is for the outbox.</summary>
public static class ScheduledPublishPolicy
{
    /// <summary>
    /// How often a pass runs. A minute is the granularity staff are promised: a record scheduled for 10:00 is
    /// published by 10:01, and the cache then catches up within <c>HybridAppCache.MaxLocalLifetime</c>.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The most records one content type contributes to one pass. A backlog (the Worker was down overnight) is
    /// drained a batch per minute rather than in one long transaction-heavy pass.
    /// </summary>
    public const int BatchSize = 50;

    /// <summary>
    /// Who the audit trail names for a publish nobody asked for in the moment. Shaped like the seeders'
    /// <c>system:</c> actors, so the importer's "edited by staff since?" check still reads it as not-a-person.
    /// </summary>
    public const string Actor = "system:scheduled-publisher";
}
