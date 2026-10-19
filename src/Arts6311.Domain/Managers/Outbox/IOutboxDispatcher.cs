namespace Arts6311.Domain.Managers.Outbox;

public sealed record OutboxDispatchSummary(int Claimed, int Completed, int Retrying, int Poisoned);

public interface IOutboxDispatcher
{
    /// <summary>
    /// Claims and delivers one batch of due messages: newly available ones, and ones whose lease expired
    /// (a prior dispatch pass or process died mid-delivery). Each message is claimed just before its handler runs,
    /// in a DI scope of its own, and its outcome (Completed, retried with backoff, or Poisoned once attempts are
    /// exhausted) is saved as soon as it is handled, so one message's exception never loses another message's
    /// already-recorded outcome in the same batch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cancelling <paramref name="cancellationToken"/> (shutdown) stops the pass: the in-flight message stays Leased
    /// with its attempt counted until its lease lapses, and later messages are left unclaimed. Any other
    /// <see cref="OperationCanceledException"/> from a handler is an ordinary failure.
    /// </para>
    /// <para>
    /// Every Pending and every Leased row is fetched to apply the due/lease-expiry check client-side (the SQLite
    /// provider the kernel tests run on cannot translate <c>DateTimeOffset</c> comparison or ordering into SQL),
    /// then only the earliest <see cref="OutboxPolicy.BatchSize"/> due ones are claimed. Correct at any backlog
    /// size; the per-pass read grows with the Pending+Leased backlog. Revisit if that backlog ever grows large
    /// enough for this to matter — nothing at this site's volume produces one.
    /// </para>
    /// </remarks>
    Task<OutboxDispatchSummary> DispatchDueAsync(CancellationToken cancellationToken);
}
