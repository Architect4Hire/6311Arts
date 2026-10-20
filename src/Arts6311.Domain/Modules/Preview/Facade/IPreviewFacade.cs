using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Preview.Managers.ServiceModels;
using Arts6311.Domain.Modules.Preview.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Preview.Facade;

/// <summary>
/// The Preview module's boundary: staff ask for a link to an unpublished record, and the site reads one back
/// (ADR 0036).
/// </summary>
public interface IPreviewFacade
{
    /// <summary>
    /// A signed, expiring link for one record. Refusals: <c>validation.failed</c> for a type previews are not made
    /// for, <c>resource.not_found</c> when there is no such record.
    /// </summary>
    Task<OperationResult<PreviewLinkServiceModel>> CreateLinkAsync(
        PreviewLinkRequestViewModel request, CancellationToken cancellationToken);

    /// <summary>
    /// The record a token names, as the site renders it. Everything unusable — expired, tampered with, not a token,
    /// or a record since deleted — is one <c>resource.not_found</c>, so a holder learns nothing from the answer.
    /// </summary>
    Task<OperationResult<ContentPreviewServiceModel>> ReadAsync(string? token, CancellationToken cancellationToken);
}
