using Arts6311.Domain.Managers.Paging;
using System.Globalization;
using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Time;
using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Data.Repositories;
using Arts6311.Domain.Modules.Forms.Managers;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Forms.Data;

/// <summary>
/// The Forms module's DataLayer: one <c>SaveChangesAsync</c> per operation, with its emails staged as
/// <c>email.send-requested</c> before it (outbox.md) and an audit row for every staff action. It stamps the
/// bookkeeping and decides nothing about forms itself.
/// </summary>
internal sealed class FormsDataLayer(
    IFormsRepository repository,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation,
    IClock clock) : IFormsDataLayer
{
    public async Task ReceiveSubmissionAsync(
        ArtistSubmission submission, string? newsletterEmail, bool confirm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        repository.Add(submission);
        if (newsletterEmail is not null)
        {
            await StageSubscriptionAsync(newsletterEmail, NewsletterSource.Submission, cancellationToken);
        }

        if (confirm)
        {
            StageEmail(FormsEmailKinds.SubmissionReceived, submission.Id);
        }

        StageEmail(FormsEmailKinds.SubmissionStaff, submission.Id);
        await SaveOrDiscardAsync(cancellationToken);
    }

    public async Task ReceiveContactAsync(ContactMessage message, bool confirm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        repository.Add(message);
        if (confirm)
        {
            StageEmail(FormsEmailKinds.ContactReceived, message.Id);
        }

        StageEmail(FormsEmailKinds.ContactStaff, message.Id);
        await SaveOrDiscardAsync(cancellationToken);
    }

    public async Task SubscribeAsync(string email, NewsletterSource source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(email);

        if (await TrySubscribeAsync(email, source, cancellationToken))
        {
            return;
        }

        // The unique index: someone signed the same address up between the read and the save. Read again and refresh
        // their consent instead; a second clash is a fault, not a race, and surfaces.
        repository.DiscardChanges();
        if (!await TrySubscribeAsync(email, source, cancellationToken))
        {
            throw new InvalidOperationException("Signing up an address clashed twice on the newsletter list.");
        }
    }

    public Task<bool> HasSubmissionFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken) =>
        repository.HasSubmissionFromAsync(email, since, cancellationToken);

    public Task<bool> HasContactFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken) =>
        repository.HasContactFromAsync(email, since, cancellationToken);

    public Task<ArtistSubmission?> FindSubmissionAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindSubmissionAsync(id, cancellationToken);

    public Task<List<ArtistSubmission>> ListSubmissionsAsync(
        ListSort sort, ListPosition? after, int take, SubmissionStatus? status, CancellationToken cancellationToken) =>
        repository.ListSubmissionsAsync(sort, after, take, status, cancellationToken);

    public async Task<FormsWriteOutcome> UpdateSubmissionAsync(
        ArtistSubmission submission, string actorUserId, string summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        submission.UpdatedAt = clock.UtcNow;
        submission.UpdatedBy = actorUserId;
        submission.RowVersion = Guid.NewGuid();
        audit.Record(new AuditEntry(
            actorUserId, FormsAudit.SubmissionUpdated, FormsAudit.SubmissionResource, submission.Id.ToString(),
            correlation.Current, summary));

        return await SaveAsync(cancellationToken);
    }

    public Task<ContactMessage?> FindContactAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindContactAsync(id, cancellationToken);

    public Task<List<ContactMessage>> ListContactsAsync(
        ListSort sort, ListPosition? after, int take, bool? handled, CancellationToken cancellationToken) =>
        repository.ListContactsAsync(sort, after, take, handled, cancellationToken);

    public async Task<FormsWriteOutcome> UpdateContactAsync(
        ContactMessage message, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.UpdatedAt = clock.UtcNow;
        message.UpdatedBy = actorUserId;
        message.RowVersion = Guid.NewGuid();
        audit.Record(new AuditEntry(
            actorUserId, FormsAudit.ContactUpdated, FormsAudit.ContactResource, message.Id.ToString(),
            correlation.Current, message.Handled ? "Marked handled." : "Marked not handled."));

        return await SaveAsync(cancellationToken);
    }

    public Task<NewsletterSubscriber?> FindSubscriberAsync(Guid id, CancellationToken cancellationToken) =>
        repository.FindSubscriberAsync(id, cancellationToken);

    public Task<List<NewsletterSubscriber>> ListSubscribersAsync(
        ListSort sort, ListPosition? after, int take, string? search, CancellationToken cancellationToken) =>
        repository.ListSubscribersAsync(sort, after, take, search, cancellationToken);

    public async Task RemoveSubscriberAsync(NewsletterSubscriber subscriber, string actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        repository.Remove(subscriber);

        // The id, not the address: the audit log outlives the subscription, and keeping the address there would keep
        // the very thing the person asked to have removed.
        audit.Record(new AuditEntry(
            actorUserId, FormsAudit.NewsletterRemoved, FormsAudit.NewsletterResource, subscriber.Id.ToString(),
            correlation.Current, "Removed an address from the newsletter list."));
        await repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<NewsletterSubscriber>> ExportSubscribersAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var subscribers = await repository.ListAllSubscribersAsync(cancellationToken);
        audit.Record(new AuditEntry(
            actorUserId, FormsAudit.NewsletterExported, FormsAudit.NewsletterResource, "export", correlation.Current,
            string.Create(CultureInfo.InvariantCulture, $"Exported the newsletter list ({subscribers.Count} addresses).")));
        await repository.SaveChangesAsync(cancellationToken);
        return subscribers;
    }

    private async Task<bool> TrySubscribeAsync(string email, NewsletterSource source, CancellationToken cancellationToken)
    {
        await StageSubscriptionAsync(email, source, cancellationToken);

        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    /// <summary>Adds the address, or refreshes the consent of the row already holding it, for the save to come.</summary>
    private async Task StageSubscriptionAsync(string email, NewsletterSource source, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (await repository.FindSubscriberByEmailAsync(email, cancellationToken) is { } existing)
        {
            existing.ConsentedAt = now;
            existing.ConsentVersion = FormsPolicy.NewsletterConsentVersion;
        }
        else
        {
            repository.Add(new NewsletterSubscriber
            {
                Id = Guid.CreateVersion7(now),
                Email = email,
                ConsentedAt = now,
                ConsentVersion = FormsPolicy.NewsletterConsentVersion,
                Source = source,
                CreatedAt = now,
            });
        }
    }

    /// <summary>One email about <paramref name="subjectId"/>, in the save about to happen. Ids only (outbox.md).</summary>
    private void StageEmail(string kind, Guid subjectId) =>
        outbox.Enqueue(
            EmailSendRequested.MessageType,
            new EmailSendRequested(kind, subjectId.ToString()).Serialize(),
            correlation.Current);

    /// <summary>
    /// Saves, or on any failure forgets what was staged before rethrowing. The context is the request's: the facade
    /// goes on to remove the pictures a failed submission stored, and a later save on the same context must not carry
    /// the failed record and its emails in with it.
    /// </summary>
    private async Task SaveOrDiscardAsync(CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            repository.DiscardChanges();
            throw;
        }
    }

    private async Task<FormsWriteOutcome> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return FormsWriteOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            repository.DiscardChanges();
            return FormsWriteOutcome.Conflict;
        }
    }
}
