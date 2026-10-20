using System.Text.Json.Serialization;

namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// Where a publishable CMS record is in its lifecycle (content.md "Lifecycle"). Stored as its number, so the values
/// never change; written to JSON by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PublishStatus>))]
public enum PublishStatus
{
    /// <summary>Staff only. Every record starts here, and so does an imported one.</summary>
    Draft = 0,

    /// <summary>Waiting for <c>ScheduledFor</c>, which the Worker's scheduled publisher acts on (ADR 0036).</summary>
    Scheduled = 10,

    Published = 20,

    /// <summary>Retired. Nothing leads out of it yet.</summary>
    Archived = 30,
}

/// <summary>The explicit status commands. A <c>PATCH</c> never changes status (content.md).</summary>
public enum PublishCommand
{
    Publish = 0,

    Unpublish = 10,

    Archive = 20,

    /// <summary>
    /// Hands the record to the scheduled publisher for a time of its own. It carries that time, so it is the one
    /// command with a body (ADR 0036).
    /// </summary>
    Schedule = 30,

    /// <summary>Takes a scheduled record back to Draft, with its time cleared.</summary>
    Unschedule = 40,
}
