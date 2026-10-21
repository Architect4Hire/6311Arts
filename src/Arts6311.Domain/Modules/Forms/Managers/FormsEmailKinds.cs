namespace Arts6311.Domain.Modules.Forms.Managers;

/// <summary>
/// The email kinds this module owns, as used in <c>EmailSendRequested.Kind</c>. Each has a composer registered under
/// the same string (add-notification).
/// </summary>
public static class FormsEmailKinds
{
    /// <summary>To the artist: we have it. Fixed words; nothing they typed.</summary>
    public const string SubmissionReceived = "forms.submission-received";

    /// <summary>To staff: a new submission, with a link to it in the CMS.</summary>
    public const string SubmissionStaff = "forms.submission-staff";

    /// <summary>To the sender: we have your message. Fixed words; nothing they typed.</summary>
    public const string ContactReceived = "forms.contact-received";

    /// <summary>To staff: the message itself, with the sender's address to reply to.</summary>
    public const string ContactStaff = "forms.contact-staff";
}
