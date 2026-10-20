using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Pages.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, with what the sanitizer removed, if anything.
/// </summary>
internal static class PageAudit
{
    public const string ResourceType = "page";

    public const string Created = "page.created";

    public const string Updated = "page.updated";

    public const string Seeded = "page.seeded";

    /// <summary>
    /// The scheduled publisher gave up on a page: what publishing needs was no longer true, so it is a Draft
    /// again (ADR 0036). Its own action, so the trail tells it apart from a person unscheduling.
    /// </summary>
    public const string ScheduleAbandoned = "page.schedule-abandoned";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "page.published",
        PublishCommand.Unpublish => "page.unpublished",
        PublishCommand.Archive => "page.archived",
        PublishCommand.Schedule => "page.scheduled",
        PublishCommand.Unschedule => "page.unscheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
