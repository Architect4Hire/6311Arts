using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, as in Exhibitions.
/// </summary>
internal static class ClassAudit
{
    /// <summary>
    /// The content type, as the cache tags, the revalidation payload and the preview map all spell it. The sessions
    /// are part of the offering, so they are audited under it rather than as a type of their own.
    /// </summary>
    public const string ResourceType = "class-offering";

    public const string Created = "class-offering.created";

    public const string Updated = "class-offering.updated";

    public const string Seeded = "class-offering.seeded";

    /// <summary>
    /// The scheduled publisher gave up on an offering: what publishing needs was no longer true, so it is a Draft
    /// again (ADR 0036). Its own action, so the trail tells it apart from a person unscheduling.
    /// </summary>
    public const string ScheduleAbandoned = "class-offering.schedule-abandoned";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "class-offering.published",
        PublishCommand.Unpublish => "class-offering.unpublished",
        PublishCommand.Archive => "class-offering.archived",
        PublishCommand.Schedule => "class-offering.scheduled",
        PublishCommand.Unschedule => "class-offering.unscheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
