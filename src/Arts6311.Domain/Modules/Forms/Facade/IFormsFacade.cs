using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Forms.Managers.ServiceModels;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Forms.Facade;

/// <summary>
/// The Forms module's boundary (ADR 0047): the three public forms, the CMS inbox and newsletter list, and this
/// module's email composers.
/// </summary>
public interface IFormsFacade
{
    /// <summary>
    /// <c>POST /forms/submissions</c>: an artist's submission with up to six pictures, which go into Media as hidden
    /// drafts. The emails are staged in the same save. A honeypot hit succeeds and keeps nothing. Refusals:
    /// <c>validation.failed</c>, and Media's own codes for a picture it can't take (<c>media.too_large</c>,
    /// <c>media.type_unsupported</c>, …); a refused submission leaves none of its new pictures behind.
    /// </summary>
    /// <param name="images">The picture parts, read lazily as the request streams; enumerated only once the answers are valid.</param>
    Task<OperationResult> SubmitAsync(
        ArtistSubmissionViewModel submission, IAsyncEnumerable<SubmissionImageViewModel> images, CancellationToken cancellationToken);

    /// <summary><c>POST /forms/contact</c>. A honeypot hit succeeds and keeps nothing. Refusal: <c>validation.failed</c>.</summary>
    Task<OperationResult> ContactAsync(ContactMessageViewModel message, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /forms/newsletter</c>. The same success whether the address is new, already on the list or a honeypot
    /// hit, so the answer says nothing about who is subscribed. Refusal: <c>validation.failed</c>.
    /// </summary>
    Task<OperationResult> SubscribeAsync(NewsletterSignupViewModel signup, CancellationToken cancellationToken);

    Task<OperationResult<CursorPage<SubmissionSummaryServiceModel>>> ListSubmissionsAsync(
        SubmissionListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<SubmissionServiceModel>> GetSubmissionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Status and notes. Requires the ETag as <paramref name="ifMatch"/>.</summary>
    Task<OperationResult<SubmissionServiceModel>> UpdateSubmissionAsync(
        Guid id, SubmissionUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<CursorPage<ContactMessageServiceModel>>> ListContactsAsync(
        ContactListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ContactMessageServiceModel>> GetContactAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Handled or not. Requires the ETag as <paramref name="ifMatch"/>.</summary>
    Task<OperationResult<ContactMessageServiceModel>> UpdateContactAsync(
        Guid id, ContactUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<CursorPage<NewsletterSubscriberServiceModel>>> ListSubscribersAsync(
        NewsletterListViewModel request, CancellationToken cancellationToken);

    /// <summary>Takes an address off the list (Admin, audited).</summary>
    Task<OperationResult> RemoveSubscriberAsync(Guid id, string actorUserId, CancellationToken cancellationToken);

    /// <summary>The whole list as CSV (Admin, audited): personal data leaving the system.</summary>
    Task<NewsletterExportServiceModel> ExportSubscribersAsync(string actorUserId, CancellationToken cancellationToken);
}
