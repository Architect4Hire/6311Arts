using Arts6311.Domain.Managers.Publishing;

namespace Arts6311.Domain.Modules.Artists.Managers;

/// <summary>
/// The audit vocabulary this module writes. Status transitions are audited (content.md), archive among them
/// (auth.md "Sensitive operations"); creates and edits are too, as in Media.
/// </summary>
internal static class ArtistAudit
{
    public const string ResourceType = "artist";

    public const string Created = "artist.created";

    public const string Updated = "artist.updated";

    public const string Seeded = "artist.seeded";

    /// <summary>
    /// A record staff deleted (ADR 0052). Admin-only and audited, as every delete is (auth.md "Sensitive
    /// operations"); the row is what says it existed at all, since nothing else shows it any more.
    /// </summary>
    public const string Deleted = "artist.deleted";

    /// <summary>An Admin brought a deleted record back, as a Draft (ADR 0052).</summary>
    public const string Restored = "artist.restored";

    /// <summary>
    /// The scheduled publisher gave up on an artist: what publishing needs was no longer true, so it is a Draft
    /// again (ADR 0036). Its own action, so the trail tells it apart from a person unscheduling.
    /// </summary>
    public const string ScheduleAbandoned = "artist.schedule-abandoned";

    public static string For(PublishCommand command) => command switch
    {
        PublishCommand.Publish => "artist.published",
        PublishCommand.Unpublish => "artist.unpublished",
        PublishCommand.Archive => "artist.archived",
        PublishCommand.Schedule => "artist.scheduled",
        PublishCommand.Unschedule => "artist.unscheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
