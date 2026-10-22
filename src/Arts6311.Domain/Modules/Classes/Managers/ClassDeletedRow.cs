using Arts6311.Domain.Managers.Concurrency;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// One row of the Deleted view as the repository reads it (ADR 0052). <paramref name="Status"/> is what the
/// class was when it was deleted, not a state it is in now, and the row version is what a restore sends back
/// as <c>If-Match</c>.
/// </summary>
/// <remarks>
/// A type of its own rather than the ServiceModel, because the row version is a <c>Guid</c> here and an
/// <c>ETag</c> string in the answer, and <c>EntityTag.Format</c> is not something SQL can do.
/// </remarks>
internal sealed record ClassDeletedRow(
    Guid Id,
    string Title,
    string Slug,
    PublishStatus Status,
    DateTimeOffset DeletedAt,
    Guid RowVersion)
{
    public ClassDeletedServiceModel ToServiceModel() =>
        new(Id, Title, Slug, Status, DeletedAt, EntityTag.Format(RowVersion));
}
