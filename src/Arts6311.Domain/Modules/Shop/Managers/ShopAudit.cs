using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Shop.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, as in Classes.
/// </summary>
internal static class ShopAudit
{
    /// <summary>
    /// The content type, as the cache tags, the revalidation payload and the preview map all spell it.
    /// </summary>
    public const string ResourceType = "shop-item";

    public const string Created = "shop-item.created";

    public const string Updated = "shop-item.updated";

    public const string Seeded = "shop-item.seeded";

    /// <summary>
    /// A record staff deleted (ADR 0052). Admin-only and audited, as every delete is (auth.md "Sensitive
    /// operations"); the row is what says it existed at all, since nothing else shows it any more.
    /// </summary>
    public const string Deleted = "shop-item.deleted";

    /// <summary>An Admin brought a deleted record back, as a Draft (ADR 0052).</summary>
    public const string Restored = "shop-item.restored";

    /// <summary>
    /// The scheduled publisher gave up on an item: what publishing needs was no longer true, so it is a Draft again
    /// (ADR 0036). Its own action, so the trail tells it apart from a person unscheduling.
    /// </summary>
    public const string ScheduleAbandoned = "shop-item.schedule-abandoned";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "shop-item.published",
        PublishCommand.Unpublish => "shop-item.unpublished",
        PublishCommand.Archive => "shop-item.archived",
        PublishCommand.Schedule => "shop-item.scheduled",
        PublishCommand.Unschedule => "shop-item.unscheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
