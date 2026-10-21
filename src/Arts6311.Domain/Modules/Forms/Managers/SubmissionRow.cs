using Arts6311.Domain.Modules.Forms.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>
/// One submission as Business hands it to the facade: everything but the pictures, which the facade asks Media about
/// (facade to facade, backend.md) before it becomes a <see cref="SubmissionServiceModel"/>.
/// </summary>
internal sealed record SubmissionRow(
    Guid Id,
    string Name,
    string Email,
    IReadOnlyList<string> Wants,
    SubmissionAnswersServiceModel Answers,
    IReadOnlyList<Guid> ImageIds,
    SubmissionStatus Status,
    string? Notes,
    DateTimeOffset ReceivedAt,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    string ETag)
{
    public SubmissionServiceModel ToServiceModel(IReadOnlyList<SubmissionImageServiceModel> images) => new(
        Id, Name, Email, Wants, Answers, images, Status, Notes, ReceivedAt, UpdatedAt, UpdatedBy, ETag);
}
