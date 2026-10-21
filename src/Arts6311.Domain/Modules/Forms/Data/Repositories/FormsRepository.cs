using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Managers;
using Microsoft.EntityFrameworkCore;

namespace Arts6311.Domain.Modules.Forms.Data.Repositories;

internal sealed class FormsRepository(ArtsDbContext context) : IFormsRepository
{
    private const char LikeEscape = '\\';

    private DbSet<ArtistSubmission> Submissions => context.Set<ArtistSubmission>();

    private DbSet<ContactMessage> Contacts => context.Set<ContactMessage>();

    private DbSet<NewsletterSubscriber> Subscribers => context.Set<NewsletterSubscriber>();

    public void Add(ArtistSubmission submission) => Submissions.Add(submission);

    public void Add(ContactMessage message) => Contacts.Add(message);

    public void Add(NewsletterSubscriber subscriber) => Subscribers.Add(subscriber);

    public void Remove(NewsletterSubscriber subscriber) => Subscribers.Remove(subscriber);

    public Task<ArtistSubmission?> FindSubmissionAsync(Guid id, CancellationToken cancellationToken) =>
        Submissions.Include(submission => submission.Images)
            .FirstOrDefaultAsync(submission => submission.Id == id, cancellationToken);

    public Task<List<ArtistSubmission>> ListSubmissionsAsync(
        FormsListPosition? after, int take, SubmissionStatus? status, CancellationToken cancellationToken)
    {
        var query = Submissions.AsNoTracking().Include(submission => submission.Images).AsQueryable();

        if (status is { } wanted)
        {
            query = query.Where(submission => submission.Status == wanted);
        }

        // Newest first, the id breaking ties, and the WHERE resuming in exactly the ORDER BY's terms.
        if (after is not null)
        {
            query = query.Where(submission => submission.ReceivedAt < after.At
                || (submission.ReceivedAt == after.At && submission.Id.CompareTo(after.Id) < 0));
        }

        return query
            .OrderByDescending(submission => submission.ReceivedAt)
            .ThenByDescending(submission => submission.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    // The time first, so the indexed range does the narrowing; the address as stored keeps its case, hence ToLower,
    // which EF translates to SQL's LOWER.
#pragma warning disable CA1304, CA1311, CA1862
    public Task<bool> HasSubmissionFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken) =>
        Submissions.AsNoTracking()
            .AnyAsync(submission => submission.ReceivedAt >= since && submission.Email.ToLower() == email, cancellationToken);

    public Task<bool> HasContactFromAsync(string email, DateTimeOffset since, CancellationToken cancellationToken) =>
        Contacts.AsNoTracking()
            .AnyAsync(message => message.ReceivedAt >= since && message.Email.ToLower() == email, cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

    public Task<ContactMessage?> FindContactAsync(Guid id, CancellationToken cancellationToken) =>
        Contacts.FirstOrDefaultAsync(message => message.Id == id, cancellationToken);

    public Task<List<ContactMessage>> ListContactsAsync(
        FormsListPosition? after, int take, bool? handled, CancellationToken cancellationToken)
    {
        var query = Contacts.AsNoTracking();

        if (handled is { } wanted)
        {
            query = query.Where(message => message.Handled == wanted);
        }

        if (after is not null)
        {
            query = query.Where(message => message.ReceivedAt < after.At
                || (message.ReceivedAt == after.At && message.Id.CompareTo(after.Id) < 0));
        }

        return query
            .OrderByDescending(message => message.ReceivedAt)
            .ThenByDescending(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<NewsletterSubscriber?> FindSubscriberByEmailAsync(string email, CancellationToken cancellationToken) =>
        Subscribers.FirstOrDefaultAsync(subscriber => subscriber.Email == email, cancellationToken);

    public Task<NewsletterSubscriber?> FindSubscriberAsync(Guid id, CancellationToken cancellationToken) =>
        Subscribers.FirstOrDefaultAsync(subscriber => subscriber.Id == id, cancellationToken);

    public Task<List<NewsletterSubscriber>> ListSubscribersAsync(
        FormsListPosition? after, int take, string? search, CancellationToken cancellationToken)
    {
        var query = Subscribers.AsNoTracking();

        if (!string.IsNullOrEmpty(search))
        {
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(subscriber => EF.Functions.Like(subscriber.Email, pattern, LikeEscape.ToString()));
        }

        if (after is not null)
        {
            query = query.Where(subscriber => subscriber.CreatedAt < after.At
                || (subscriber.CreatedAt == after.At && subscriber.Id.CompareTo(after.Id) < 0));
        }

        return query
            .OrderByDescending(subscriber => subscriber.CreatedAt)
            .ThenByDescending(subscriber => subscriber.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<List<NewsletterSubscriber>> ListAllSubscribersAsync(CancellationToken cancellationToken) =>
        Subscribers.AsNoTracking()
            .OrderBy(subscriber => subscriber.CreatedAt)
            .ThenBy(subscriber => subscriber.Id)
            .ToListAsync(cancellationToken);

    public void DiscardChanges() => context.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal)
        .Replace("[", $"{LikeEscape}[", StringComparison.Ordinal);
}
