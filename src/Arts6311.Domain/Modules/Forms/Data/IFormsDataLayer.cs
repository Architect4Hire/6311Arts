using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Data.Repositories;
using Arts6311.Domain.Modules.Forms.Managers;

namespace Arts6311.Domain.Modules.Forms.Data;

/// <summary>What a staff write came to.</summary>
internal enum FormsWriteOutcome
{
    Saved,

    /// <summary>Someone else changed the record since it was read.</summary>
    Conflict,
}

internal interface IFormsDataLayer
{
    /// <summary>
    /// Saves a new submission with its two emails (the artist's confirmation and staff's notification) staged in the
    /// same save, and — when the artist ticked the box — their newsletter sign-up too, so all of it commits together
    /// or none of it does.
    /// </summary>
    /// <param name="newsletterEmail">The address to sign up, lower-cased; <c>null</c> when they didn't tick the box.</param>
    /// <param name="confirm">Whether the artist's confirmation is staged too; staff's notification always is.</param>
    Task ReceiveSubmissionAsync(
        ArtistSubmission submission, string? newsletterEmail, bool confirm, CancellationToken cancellationToken);

    /// <summary>Saves a new contact message with staff's notification, and the sender's confirmation when asked.</summary>
    Task ReceiveContactAsync(ContactMessage message, bool confirm, CancellationToken cancellationToken);

    Task<bool> HasSubmissionFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken);

    Task<bool> HasContactFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the address, or refreshes the consent of the row already holding it. Two sign-ups racing on one address
    /// end as one row, the later consent kept.
    /// </summary>
    Task SubscribeAsync(string email, NewsletterSource source, CancellationToken cancellationToken);

    Task<ArtistSubmission?> FindSubmissionAsync(Guid id, CancellationToken cancellationToken);

    Task<List<ArtistSubmission>> ListSubmissionsAsync(
        FormsListPosition? after, int take, SubmissionStatus? status, CancellationToken cancellationToken);

    /// <summary>A staff change to a submission, audited, under its row version.</summary>
    Task<FormsWriteOutcome> UpdateSubmissionAsync(
        ArtistSubmission submission, string actorUserId, string summary, CancellationToken cancellationToken);

    Task<ContactMessage?> FindContactAsync(Guid id, CancellationToken cancellationToken);

    Task<List<ContactMessage>> ListContactsAsync(
        FormsListPosition? after, int take, bool? handled, CancellationToken cancellationToken);

    Task<FormsWriteOutcome> UpdateContactAsync(ContactMessage message, string actorUserId, CancellationToken cancellationToken);

    Task<NewsletterSubscriber?> FindSubscriberAsync(Guid id, CancellationToken cancellationToken);

    Task<List<NewsletterSubscriber>> ListSubscribersAsync(
        FormsListPosition? after, int take, string? search, CancellationToken cancellationToken);

    /// <summary>Takes an address off the list, audited.</summary>
    Task RemoveSubscriberAsync(NewsletterSubscriber subscriber, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Every subscriber, oldest first, and an audit row saying who took the copy and how big it was.</summary>
    Task<List<NewsletterSubscriber>> ExportSubscribersAsync(string actorUserId, CancellationToken cancellationToken);
}
