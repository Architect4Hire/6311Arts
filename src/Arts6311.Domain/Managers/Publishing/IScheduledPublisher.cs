namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// How the Worker's scheduled publisher reaches one content type (ADR 0036). A kernel port implemented by each
/// publishable module, as <c>IOutboxMessageHandler</c> and <c>IDataSeeder</c> are: the kernel declares the shape
/// and depends on no module, and a new content type is one more registration.
/// </summary>
/// <remarks>
/// The implementation is the module's facade, or a thin adapter over it: a background job works through facades
/// and never touches a DbContext, a repository or a gateway (add-background-job step 3).
/// </remarks>
public interface IScheduledPublisher
{
    /// <summary>The content type, as its cache tag and audit rows spell it: <c>artist</c>, <c>exhibition</c>, <c>page</c>.</summary>
    string ContentType { get; }

    /// <summary>
    /// The records that are Scheduled for <paramref name="asOf"/> or earlier, oldest first, at most
    /// <paramref name="take"/> of them. A read: what a pass then does with each is <see cref="PublishDueAsync"/>'s.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes one due record, having read it again: the claim is its row version, so of two Workers acting on
    /// the same record exactly one save succeeds and the other is told it lost (ADR 0036).
    /// </summary>
    Task<ScheduledPublishOutcome> PublishDueAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken);
}

/// <summary>What one unit of a scheduled-publish pass did, for the pass's counters and logs.</summary>
public enum ScheduledPublishOutcome
{
    /// <summary>It is published.</summary>
    Published = 0,

    /// <summary>Nothing to do: staff moved, rescheduled or archived it since the due list was read.</summary>
    Skipped = 10,

    /// <summary>Another Worker or a staff write saved first. The record is in their hands, not ours.</summary>
    Lost = 20,

    /// <summary>
    /// What publishing requires is no longer true (an image lost its alt text), so the record went back to Draft
    /// with its time cleared and an audit row saying why, rather than being retried every minute for ever.
    /// </summary>
    Unscheduled = 30,
}
