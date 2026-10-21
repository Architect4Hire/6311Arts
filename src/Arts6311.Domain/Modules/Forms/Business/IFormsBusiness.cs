using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Forms.Managers;
using Arts6311.Domain.Modules.Forms.Managers.ServiceModels;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;

namespace Arts6311.Domain.Modules.Forms.Business;

internal interface IFormsBusiness
{
    /// <summary>
    /// Stores a validated submission with its pictures (already in Media) and stages its two emails; then, when the
    /// artist ticked the box, puts them on the newsletter list. Returns the new submission's id.
    /// </summary>
    Task<Guid> ReceiveSubmissionAsync(
        ArtistSubmissionViewModel submission, IReadOnlyList<Guid> imageIds, CancellationToken cancellationToken);

    /// <summary>Stores a validated contact message and stages its two emails.</summary>
    Task<Guid> ReceiveContactAsync(ContactMessageViewModel message, CancellationToken cancellationToken);

    /// <summary>Adds a validated address to the newsletter list, or refreshes its consent.</summary>
    Task SubscribeAsync(NewsletterSignupViewModel signup, CancellationToken cancellationToken);

    Task<OperationResult<CursorPage<SubmissionSummaryServiceModel>>> ListSubmissionsAsync(
        SubmissionListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<SubmissionRow>> GetSubmissionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Status and notes, under the row version staff read.</summary>
    Task<OperationResult<SubmissionRow>> UpdateSubmissionAsync(
        Guid id, SubmissionUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<CursorPage<ContactMessageServiceModel>>> ListContactsAsync(
        ContactListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult<ContactMessageServiceModel>> GetContactAsync(Guid id, CancellationToken cancellationToken);

    Task<OperationResult<ContactMessageServiceModel>> UpdateContactAsync(
        Guid id, ContactUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken);

    Task<OperationResult<CursorPage<NewsletterSubscriberServiceModel>>> ListSubscribersAsync(
        NewsletterListViewModel request, CancellationToken cancellationToken);

    Task<OperationResult> RemoveSubscriberAsync(Guid id, string actorUserId, CancellationToken cancellationToken);

    Task<NewsletterExportServiceModel> ExportSubscribersAsync(string actorUserId, CancellationToken cancellationToken);
}
