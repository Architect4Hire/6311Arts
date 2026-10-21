namespace Arts6311.Domain.Modules.Classes.Data.Entities;

/// <summary>
/// One sitting of a run: when it meets. Nothing is sold by the sitting — the seat, its SKU and its price are the
/// run's (ADR 0044) — so a session is only a time, shown in its run's display zone.
/// </summary>
public sealed class ClassSession
{
    public Guid Id { get; set; }

    public Guid ClassRunId { get; set; }

    /// <summary>When it starts. UTC, as everything stored is (backend.md).</summary>
    public DateTimeOffset StartsAt { get; set; }

    /// <summary>When it ends. After <see cref="StartsAt"/>.</summary>
    public DateTimeOffset EndsAt { get; set; }
}
