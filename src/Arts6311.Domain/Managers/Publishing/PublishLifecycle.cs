namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// The transition table every publishable type shares (content.md "Lifecycle"):
/// <code>
/// Draft ──schedule──▶ Scheduled ──publish──▶ Published ──unpublish──▶ Draft
///   │    ◀─unschedule─────┘           ▲
///   └──────publish────────────────────┘
///   └──────archive────────┴───────────┴──▶ Archived
/// </code>
/// A Scheduled record is published by the Worker's scheduled publisher when its time comes (ADR 0036), or by
/// staff at once; it can also be rescheduled to another time. Anything else — publishing what is already
/// published, scheduling a published record, leaving Archived — is refused, so a status command never silently
/// does nothing.
/// </summary>
public static class PublishLifecycle
{
    /// <summary>The status <paramref name="command"/> leads to from <paramref name="current"/>, or <c>null</c> when it is not allowed.</summary>
    public static PublishStatus? Next(PublishStatus current, PublishCommand command) => (current, command) switch
    {
        (PublishStatus.Draft or PublishStatus.Scheduled, PublishCommand.Publish) => PublishStatus.Published,
        (PublishStatus.Published, PublishCommand.Unpublish) => PublishStatus.Draft,
        (PublishStatus.Draft or PublishStatus.Scheduled or PublishStatus.Published, PublishCommand.Archive) => PublishStatus.Archived,

        // Scheduled → Scheduled is a reschedule: the time changes, so it is a real write, not a no-op. A published
        // record is unpublished first, which is the decision staff should have to make deliberately.
        (PublishStatus.Draft or PublishStatus.Scheduled, PublishCommand.Schedule) => PublishStatus.Scheduled,
        (PublishStatus.Scheduled, PublishCommand.Unschedule) => PublishStatus.Draft,
        _ => null,
    };

    /// <summary>The verb staff see in a refusal, for example "publish".</summary>
    public static string Verb(this PublishCommand command) => command switch
    {
        PublishCommand.Publish => "publish",
        PublishCommand.Unpublish => "unpublish",
        PublishCommand.Archive => "archive",
        PublishCommand.Schedule => "schedule",
        PublishCommand.Unschedule => "unschedule",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
