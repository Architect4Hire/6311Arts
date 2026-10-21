using Arts6311.Domain.Modules.Forms.Managers;

namespace Arts6311.Domain.Modules.Forms.Data.Entities;

/// <summary>
/// An artist's submission from the public form (ADR 0047): who they are, what they are here for, their answers and
/// their pictures, and where staff have got to with it.
/// </summary>
public sealed class ArtistSubmission
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>As the artist typed it, trimmed: the address staff write back to.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Their goals, comma-separated in <see cref="FormsPolicy.Goals"/> order: <c>sell,teach</c>.</summary>
    public string Wants { get; set; } = string.Empty;

    /// <summary>Everything else they answered (<see cref="SubmissionAnswers"/>), as JSON. Read, never queried.</summary>
    public string AnswersJson { get; set; } = string.Empty;

    public SubmissionStatus Status { get; set; }

    /// <summary>Staff's own notes. Never in an email (add-notification).</summary>
    public string? Notes { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The staff member behind the last change; <c>null</c> until staff touch it.</summary>
    public string? UpdatedBy { get; set; }

    public Guid RowVersion { get; set; }

    /// <summary>The pictures, in the order they were sent.</summary>
    public List<ArtistSubmissionImage> Images { get; set; } = [];
}
