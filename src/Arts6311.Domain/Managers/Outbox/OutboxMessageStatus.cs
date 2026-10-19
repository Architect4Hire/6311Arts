namespace Arts6311.Domain.Managers.Outbox;

/// <summary>Lifecycle of one <see cref="OutboxMessage"/>. Ordered, not a bitmask.</summary>
public enum OutboxMessageStatus
{
    /// <summary>Waiting for <see cref="OutboxMessage.AvailableAt"/>, or waiting again after a failed attempt.</summary>
    Pending = 0,

    /// <summary>Claimed by a dispatcher; <see cref="OutboxMessage.LeaseExpiresAt"/> reclaims it if the lease lapses.</summary>
    Leased = 10,

    /// <summary>The handler ran successfully. Terminal.</summary>
    Completed = 20,

    /// <summary>
    /// Every attempt failed; retries are exhausted. Terminal until an Admin replays or discards it on the Sync
    /// screen, which is audited (outbox.md).
    /// </summary>
    Poisoned = 30,
}
