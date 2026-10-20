namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// The transition table every publishable type shares (content.md "Lifecycle"):
/// <code>
/// Draft ──publish──▶ Published ──unpublish──▶ Draft
///   │                    │
///   └──────archive───────┴──▶ Archived
/// </code>
/// A Scheduled record may be published (the scheduled publisher, 2.8) or archived. Anything else — publishing what
/// is already published, leaving Archived — is refused, so a status command never silently does nothing.
/// </summary>
public static class PublishLifecycle
{
    /// <summary>The status <paramref name="command"/> leads to from <paramref name="current"/>, or <c>null</c> when it is not allowed.</summary>
    public static PublishStatus? Next(PublishStatus current, PublishCommand command) => (current, command) switch
    {
        (PublishStatus.Draft or PublishStatus.Scheduled, PublishCommand.Publish) => PublishStatus.Published,
        (PublishStatus.Published, PublishCommand.Unpublish) => PublishStatus.Draft,
        (PublishStatus.Draft or PublishStatus.Scheduled or PublishStatus.Published, PublishCommand.Archive) => PublishStatus.Archived,
        _ => null,
    };

    /// <summary>The verb staff see in a refusal, for example "publish".</summary>
    public static string Verb(this PublishCommand command) => command switch
    {
        PublishCommand.Publish => "publish",
        PublishCommand.Unpublish => "unpublish",
        PublishCommand.Archive => "archive",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
