using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Memberships.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, as in Classes and Shop.
/// </summary>
internal static class MembershipAudit
{
    /// <summary>
    /// The content type, as the cache tags, the revalidation payload and the preview map all spell it.
    /// </summary>
    public const string ResourceType = "membership-plan";

    public const string Created = "membership-plan.created";

    public const string Updated = "membership-plan.updated";

    public const string Seeded = "membership-plan.seeded";

    /// <summary>
    /// A record staff deleted (ADR 0052). Admin-only and audited, as every delete is (auth.md "Sensitive
    /// operations"); the row is what says it existed at all, since nothing else shows it any more.
    /// </summary>
    public const string Deleted = "membership-plan.deleted";

    /// <summary>An Admin brought a deleted record back, as a Draft (ADR 0052).</summary>
    public const string Restored = "membership-plan.restored";

    /// <summary>
    /// The scheduled publisher gave up on a plan: what publishing needs was no longer true, so it is a Draft again
    /// (ADR 0036). Its own action, so the trail tells it apart from a person unscheduling.
    /// </summary>
    public const string ScheduleAbandoned = "membership-plan.schedule-abandoned";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "membership-plan.published",
        PublishCommand.Unpublish => "membership-plan.unpublished",
        PublishCommand.Archive => "membership-plan.archived",
        PublishCommand.Schedule => "membership-plan.scheduled",
        PublishCommand.Unschedule => "membership-plan.unscheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
