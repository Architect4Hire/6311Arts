using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Forms.Data.Entities;

namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>
/// The sortable columns of the three Forms inboxes (ADR 0062), each newest first unless asked, with ties broken on
/// the id descending as these lists always have. The repository returns the entities themselves, so a row's key is
/// read off the entity it was queried from.
/// </summary>
internal static class FormsListOrder
{
    private const int MessagePrefix = 100;

    /// <summary>"wants" orders by the choices as stored, a comma-separated list.</summary>
    public static readonly ListOrder<ArtistSubmission, ArtistSubmission> Submissions =
        new ListOrder<ArtistSubmission, ArtistSubmission>(submission => submission.Id, row => row.Id, ListSort.Descending("received"), SortDirection.Descending)
            .By("name", submission => submission.Name, row => row.Name)
            .By("email", submission => submission.Email, row => row.Email)
            .By("wants", submission => submission.Wants, row => row.Wants)
            .By("status", submission => submission.Status, row => row.Status)
            .By("pictures", submission => submission.Images.Count, row => row.Images.Count)
            .By("received", submission => submission.ReceivedAt, row => row.ReceivedAt);

    /// <summary>
    /// "message" orders by the message's first <see cref="MessagePrefix"/> characters, so the cursor carries a short
    /// value rather than the whole text; "status" puts new ones before handled ones.
    /// </summary>
    public static readonly ListOrder<ContactMessage, ContactMessage> Contacts =
        new ListOrder<ContactMessage, ContactMessage>(message => message.Id, row => row.Id, ListSort.Descending("received"), SortDirection.Descending)
            .By("from", message => message.Name, row => row.Name)
            .By("message", message => message.Message.Substring(0, MessagePrefix), row => Prefix(row.Message))
            .By("status", message => message.Handled, row => row.Handled)
            .By("received", message => message.ReceivedAt, row => row.ReceivedAt);

    /// <summary>Newest sign-up first unless asked: "created", which no column shows, is the row's own time.</summary>
    public static readonly ListOrder<NewsletterSubscriber, NewsletterSubscriber> Subscribers =
        new ListOrder<NewsletterSubscriber, NewsletterSubscriber>(
                subscriber => subscriber.Id, row => row.Id, ListSort.Descending("created"), SortDirection.Descending)
            .By("created", subscriber => subscriber.CreatedAt, row => row.CreatedAt)
            .By("email", subscriber => subscriber.Email, row => row.Email)
            .By("consented", subscriber => subscriber.ConsentedAt, row => row.ConsentedAt)
            .By("wording", subscriber => subscriber.ConsentVersion, row => row.ConsentVersion)
            .By("source", subscriber => subscriber.Source, row => row.Source);

    // SQL's SUBSTRING, which stops at the end of a shorter text rather than throwing.
    private static string Prefix(string text) => text.Length > MessagePrefix ? text[..MessagePrefix] : text;
}
