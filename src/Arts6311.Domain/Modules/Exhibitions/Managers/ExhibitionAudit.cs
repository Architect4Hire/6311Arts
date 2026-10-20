using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, as in Artists.
/// </summary>
internal static class ExhibitionAudit
{
    public const string ResourceType = "exhibition";

    public const string Created = "exhibition.created";

    public const string Updated = "exhibition.updated";

    public const string Seeded = "exhibition.seeded";

    /// <summary>
    /// The scheduled publisher gave up on an exhibition: what publishing needs was no longer true, so it is a
    /// Draft again (ADR 0036). Its own action, so the trail tells it apart from a person unscheduling.
    /// </summary>
    public const string ScheduleAbandoned = "exhibition.schedule-abandoned";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "exhibition.published",
        PublishCommand.Unpublish => "exhibition.unpublished",
        PublishCommand.Archive => "exhibition.archived",
        PublishCommand.Schedule => "exhibition.scheduled",
        PublishCommand.Unschedule => "exhibition.unscheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
