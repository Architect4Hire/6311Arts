using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Managers;

namespace Arts6311.Domain.Modules.Forms.Data.Repositories;

internal interface IFormsRepository
{
    void Add(ArtistSubmission submission);

    void Add(ContactMessage message);

    void Add(NewsletterSubscriber subscriber);

    void Remove(NewsletterSubscriber subscriber);

    /// <summary>A submission with its pictures, tracked.</summary>
    Task<ArtistSubmission?> FindSubmissionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of the inbox in <paramref name="sort"/>'s order (ADR 0062), with each submission's pictures.</summary>
    Task<List<ArtistSubmission>> ListSubmissionsAsync(
        ListSort sort, ListPosition? after, int take, SubmissionStatus? status, CancellationToken cancellationToken);

    /// <summary>Whether a submission from this address (lower-cased) has arrived since <paramref name="since"/>.</summary>
    Task<bool> HasSubmissionFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Whether a contact message from this address (lower-cased) has arrived since <paramref name="since"/>.</summary>
    Task<bool> HasContactFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken);

    Task<ContactMessage?> FindContactAsync(Guid id, CancellationToken cancellationToken);

    Task<List<ContactMessage>> ListContactsAsync(
        ListSort sort, ListPosition? after, int take, bool? handled, CancellationToken cancellationToken);

    /// <summary>The subscriber with this address (already lower-cased), tracked.</summary>
    Task<NewsletterSubscriber?> FindSubscriberByEmailAsync(string email, CancellationToken cancellationToken);

    Task<NewsletterSubscriber?> FindSubscriberAsync(Guid id, CancellationToken cancellationToken);

    Task<List<NewsletterSubscriber>> ListSubscribersAsync(
        ListSort sort, ListPosition? after, int take, string? search, CancellationToken cancellationToken);

    /// <summary>Every subscriber, oldest first, for the export.</summary>
    Task<List<NewsletterSubscriber>> ListAllSubscribersAsync(CancellationToken cancellationToken);

    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
