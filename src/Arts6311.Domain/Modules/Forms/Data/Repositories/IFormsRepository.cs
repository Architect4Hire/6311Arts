using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Managers;

namespace Arts6311.Domain.Modules.Forms.Data.Repositories;

/// <summary>Where a newest-first list resumes: the last row's time, and its id to break ties.</summary>
internal sealed record FormsListPosition(DateTimeOffset At, Guid Id);

internal interface IFormsRepository
{
    void Add(ArtistSubmission submission);

    void Add(ContactMessage message);

    void Add(NewsletterSubscriber subscriber);

    void Remove(NewsletterSubscriber subscriber);

    /// <summary>A submission with its pictures, tracked.</summary>
    Task<ArtistSubmission?> FindSubmissionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>One page of the inbox, newest first, with each submission's pictures.</summary>
    Task<List<ArtistSubmission>> ListSubmissionsAsync(
        FormsListPosition? after, int take, SubmissionStatus? status, CancellationToken cancellationToken);

    /// <summary>Whether a submission from this address (lower-cased) has arrived since <paramref name="since"/>.</summary>
    Task<bool> HasSubmissionFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Whether a contact message from this address (lower-cased) has arrived since <paramref name="since"/>.</summary>
    Task<bool> HasContactFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken);

    Task<ContactMessage?> FindContactAsync(Guid id, CancellationToken cancellationToken);

    Task<List<ContactMessage>> ListContactsAsync(
        FormsListPosition? after, int take, bool? handled, CancellationToken cancellationToken);

    /// <summary>The subscriber with this address (already lower-cased), tracked.</summary>
    Task<NewsletterSubscriber?> FindSubscriberByEmailAsync(string email, CancellationToken cancellationToken);

    Task<NewsletterSubscriber?> FindSubscriberAsync(Guid id, CancellationToken cancellationToken);

    Task<List<NewsletterSubscriber>> ListSubscribersAsync(
        FormsListPosition? after, int take, string? search, CancellationToken cancellationToken);

    /// <summary>Every subscriber, oldest first, for the export.</summary>
    Task<List<NewsletterSubscriber>> ListAllSubscribersAsync(CancellationToken cancellationToken);

    void DiscardChanges();

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
