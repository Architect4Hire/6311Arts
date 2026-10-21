namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>
/// The audit vocabulary this module writes. Staff actions on what visitors sent are audited: the records hold people's
/// contact details, and the newsletter export takes a copy of them out of the system (ADR 0047).
/// </summary>
internal static class FormsAudit
{
    public const string SubmissionResource = "artist-submission";

    public const string ContactResource = "contact-message";

    public const string NewsletterResource = "newsletter-subscriber";

    public const string SubmissionUpdated = "artist-submission.updated";

    public const string ContactUpdated = "contact-message.updated";

    public const string NewsletterRemoved = "newsletter-subscriber.removed";

    public const string NewsletterExported = "newsletter.exported";
}
