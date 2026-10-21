using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Instructors.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, as in Media.
/// </summary>
internal static class InstructorAudit
{
    public const string ResourceType = "instructor";

    public const string Created = "instructor.created";

    public const string Updated = "instructor.updated";

    public const string Seeded = "instructor.seeded";

    /// <summary>
    /// The scheduled publisher gave up on an instructor: what publishing needs was no longer true, so it is a Draft
    /// again (ADR 0036). Its own action, so the trail tells it apart from a person unscheduling.
    /// </summary>
    public const string ScheduleAbandoned = "instructor.schedule-abandoned";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "instructor.published",
        PublishCommand.Unpublish => "instructor.unpublished",
        PublishCommand.Archive => "instructor.archived",
        PublishCommand.Schedule => "instructor.scheduled",
        PublishCommand.Unschedule => "instructor.unscheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
